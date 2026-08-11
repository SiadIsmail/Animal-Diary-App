// ═══════════════════════════════════════════════════════════════════════════
//  RevenueCat webhook → profiles.entitlement_active
//
//  The ONLY writer of the server-side entitlement. It exists for one reason: a
//  caregiver's device needs to know whether SOMEONE ELSE (their pet's owner) is
//  currently subscribed, and only the server can answer that. A client-asserted
//  entitlement would let a patched app sponsor unlimited caregivers.
//
//  Note what this is NOT: it is never consulted for your own access. Your own
//  gate stays local (RevenueCat's on-device cache + the app-side trial), so the
//  app keeps working offline and someone who never makes an account is entirely
//  unaffected by this function. See AI/design-decisions.md.
//
//  Two properties this function must keep (see migration 0011):
//   • ORDER-INDEPENDENT. Retries and out-of-order delivery are normal. Every
//     write is guarded by an event-time watermark, so a stale EXPIRATION can
//     never revoke an owner who has since renewed.
//   • FAIL-SAFE, NOT FAIL-OPEN. A permanently bad payload is accepted (200) and
//     logged rather than retried forever; only transient faults return 500.
//
//  Deploy:  supabase functions deploy revenuecat-webhook --no-verify-jwt
//  Secrets: supabase secrets set REVENUECAT_WEBHOOK_SECRET=<value>
//           (SUPABASE_URL / SUPABASE_SERVICE_ROLE_KEY are injected automatically)
//  RevenueCat: Integrations → Webhooks → URL + the same Authorization value.
//
//  --no-verify-jwt is required: RevenueCat sends its own shared secret in the
//  Authorization header, not a Supabase JWT. The secret check below is therefore
//  the only thing standing between this and an open write endpoint — it must
//  stay first, and must stay constant-time.
// ═══════════════════════════════════════════════════════════════════════════

import { createClient } from "jsr:@supabase/supabase-js@2";

// Mirrors BillingConfig.EntitlementId, plus the same "single paid tier ⇒ any active
// entitlement counts" fallback the client applies, so a dashboard rename can never
// silently strip a paying owner's carers.
const ENTITLEMENT_ID = "Felova Full";

// Events after which the user HAS access. RENEWAL/UNCANCELLATION included because a
// lapse followed by a renewal must restore sponsorship immediately.
//
// TRANSFER is deliberately NOT here: it moves a purchase between app user ids, so it must
// revoke the old owner as well as grant the new one. Handling it as a plain grant left the
// previous account active forever — and since access here decides who may SPONSOR
// caregivers, that turned one store purchase into two sponsoring accounts.
const GRANTING = new Set([
  "INITIAL_PURCHASE",
  "RENEWAL",
  "UNCANCELLATION",
  "NON_RENEWING_PURCHASE",
  "SUBSCRIPTION_EXTENDED",
]);

// Events after which they do NOT. CANCELLATION is deliberately absent: a cancelled
// subscription still runs to the end of its paid period, and revoking a carer's access
// early would be both wrong and unkind — expiry handles it when the period actually ends.
const REVOKING = new Set(["EXPIRATION", "SUBSCRIPTION_PAUSED", "REFUND"]);

// Events that count as "they bought" for creator attribution (migration 0016). Only the
// FIRST purchase: a renewal is the same sale continuing, and crediting a creator again
// every month would turn one conversion into a recurring one.
const ATTRIBUTING = new Set(["INITIAL_PURCHASE", "NON_RENEWING_PURCHASE"]);

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/// Constant-time comparison over fixed-length digests, so neither the value nor its
/// LENGTH leaks through timing.
async function secretMatches(given: string, expected: string): Promise<boolean> {
  const enc = new TextEncoder();
  const [a, b] = await Promise.all([
    crypto.subtle.digest("SHA-256", enc.encode(given)),
    crypto.subtle.digest("SHA-256", enc.encode(expected)),
  ]);
  const x = new Uint8Array(a), y = new Uint8Array(b);
  let diff = 0;
  for (let i = 0; i < x.length; i++) diff |= x[i] ^ y[i];
  return diff === 0;
}

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json" } });

Deno.serve(async (req) => {
  const expected = Deno.env.get("REVENUECAT_WEBHOOK_SECRET");
  if (!expected) {
    console.error("revenuecat-webhook: REVENUECAT_WEBHOOK_SECRET is not set");
    return json({ error: "not configured" }, 500);
  }
  if (!await secretMatches(req.headers.get("Authorization") ?? "", expected)) {
    return json({ error: "unauthorized" }, 401);
  }

  let event: Record<string, unknown>;
  try {
    event = ((await req.json())?.event ?? {}) as Record<string, unknown>;
  } catch {
    return json({ error: "bad json" }, 400);   // never retryable
  }

  const type = String(event.type ?? "");
  const environment = String(event.environment ?? "");

  // When the event was RAISED. Everything below is ordered by this, never by arrival:
  // RevenueCat retries with backoff and gives no ordering guarantee, so a retried
  // EXPIRATION can land after the RENEWAL that superseded it.
  const eventMs = Number(event.event_timestamp_ms ?? 0);
  const eventAt = new Date(eventMs > 0 ? eventMs : Date.now()).toISOString();

  const supabase = createClient(
    Deno.env.get("SUPABASE_URL")!,
    Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!,
  );

  /// Write the entitlement for one or more app user ids, ignoring any that is older than
  /// what the row already has.
  ///
  /// Returns how many rows were actually written. Zero is meaningful and always worth a
  /// look: no profile row (deleted account, or the app never called Login so the id is not
  /// a Supabase user), or a newer event already applied.
  async function setEntitlement(ids: string[], active: boolean, expiresAt: string | null) {
    // Anonymous ids belong to people with no account — they have no caregivers to sponsor.
    // Non-UUIDs would raise "invalid input syntax for type uuid", and returning 500 for a
    // payload that can never succeed would retry it forever.
    const real = ids.filter((id) => id && !id.startsWith("$RCAnonymousID") && UUID.test(id));
    const rejected = ids.filter((id) => id && !real.includes(id));
    if (rejected.length > 0)
      console.log(`revenuecat-webhook: skipping ${rejected.length} non-account id(s)`);
    if (real.length === 0)
      return { error: null, written: 0, skipped: true };

    // Update, never upsert: the profile row is created by on_auth_user_created, and an id
    // with no row means the account is gone — inserting would resurrect billing state for
    // a deleted user.
    //
    // .lte is the watermark: apply only when this event is at least as new as the one
    // already recorded. That makes the whole function order-independent and idempotent
    // under retries. The column is NOT NULL DEFAULT '-infinity' (migration 0011) precisely
    // so this stays a single typed filter — no or(), no filter-string quoting on the path
    // that must never fail open.
    const { data, error } = await supabase
      .from("profiles")
      .update({
        entitlement_active: active,
        entitlement_expires_at: active ? expiresAt : null,
        entitlement_event_at: eventAt,
        entitlement_updated_at: new Date().toISOString(),
      })
      .in("id", real)
      .lte("entitlement_event_at", eventAt)
      .select("id");

    return { error, written: data?.length ?? 0, skipped: false };
  }

  /// Credit a creator for a first purchase, if this account entered a code (migration 0016).
  ///
  /// BEST EFFORT, ALWAYS. Attribution is a marketing number; the entitlement is what decides
  /// whether a caregiver can log an insulin dose. This runs after the entitlement write and
  /// can never change the response, or a broken reporting table would start bouncing
  /// RevenueCat's retries and taking sponsorship down with it.
  ///
  /// Idempotent by construction: event_id is the primary key, so a retry of the same event
  /// collides and is ignored rather than double-crediting.
  async function recordAttribution(userId: string) {
    try {
      const { data: profile } = await supabase
        .from("profiles")
        .select("referred_code, referred_creator, referred_at")
        .eq("id", userId)
        .maybeSingle();

      if (!profile?.referred_code) return;   // no code entered: nothing to credit

      const eventId = String(event.id ?? "");
      if (!eventId) {
        console.warn("revenuecat-webhook: purchase event carried no id — attribution skipped");
        return;
      }

      const { error } = await supabase.from("creator_attributions").insert({
        event_id: eventId,
        user_id: userId,
        code: profile.referred_code,
        creator: profile.referred_creator,
        event_type: type,
        product_id: String(event.product_id ?? "") || null,
        store: String(event.store ?? "") || null,
        environment: environment || null,
        referred_at: profile.referred_at,
        purchased_at: eventAt,
      });

      // 23505 = unique_violation: this event was already credited. Expected under retries.
      if (error && error.code !== "23505") {
        console.error(`revenuecat-webhook: attribution insert failed: ${error.message}`);
        return;
      }
      if (!error)
        console.log(`revenuecat-webhook: attributed ${type} to ${profile.referred_creator}`);
    } catch (e) {
      console.error(`revenuecat-webhook: attribution threw: ${e}`);
    }
  }

  // ── TRANSFER: a purchase moved between app user ids ───────────────────────
  // Revoke first: if the grant then fails and RevenueCat retries, over-revoking is
  // recoverable (they restore) while over-granting silently hands out free access.
  if (type === "TRANSFER") {
    const from = (event.transferred_from ?? []) as string[];
    // Some payloads carry only app_user_id for the new owner; prefer the explicit list.
    const to = ((event.transferred_to ?? []) as string[]).length > 0
      ? (event.transferred_to as string[])
      : [String(event.app_user_id ?? "")].filter(Boolean);

    if (from.length === 0 && to.length === 0) {
      // Both empty means the payload does not look like we expect — most likely the field
      // names differ in this API version. Say so loudly: silently returning ok here would
      // leave a transferred purchase granting access to BOTH accounts, which is exactly
      // the bug this branch exists to prevent.
      console.error(
        `revenuecat-webhook: TRANSFER carried no transferred_from/transferred_to. ` +
          `Keys present: [${Object.keys(event).join(", ")}]. Entitlements NOT updated.`,
      );
      return json({ ok: false, type, reason: "no transfer ids in payload" });
    }

    const expires = Number(event.expiration_at_ms ?? 0);
    const revoked = await setEntitlement(from, false, null);
    if (revoked.error) {
      console.error(`revenuecat-webhook: TRANSFER revoke failed: ${revoked.error.message}`);
      return json({ error: "revoke failed" }, 500);
    }
    const granted = await setEntitlement(to, true, expires > 0 ? new Date(expires).toISOString() : null);
    if (granted.error) {
      console.error(`revenuecat-webhook: TRANSFER grant failed: ${granted.error.message}`);
      return json({ error: "grant failed" }, 500);
    }
    console.log(
      `revenuecat-webhook: TRANSFER revoked=${revoked.written} granted=${granted.written} (${environment})`,
    );
    return json({ ok: true, type, revoked: revoked.written, granted: granted.written });
  }

  // app_user_id is the Supabase user id only once the app has called Login on sign-in.
  const appUserId = String(event.app_user_id ?? "");

  let active: boolean;
  if (GRANTING.has(type)) active = true;
  else if (REVOKING.has(type)) active = false;
  else {
    // CANCELLATION, BILLING_ISSUE, PRODUCT_CHANGE, TEST… — no access change.
    // A TEST event reaching here is the success signal for webhook setup.
    return json({ ok: true, skipped: type });
  }

  const entitlementIds = (event.entitlement_ids ?? []) as string[];
  if (active && entitlementIds.length > 0 && !entitlementIds.includes(ENTITLEMENT_ID)) {
    console.warn(
      `revenuecat-webhook: '${ENTITLEMENT_ID}' not among [${entitlementIds}] — ` +
        "granting anyway (single paid tier). Align the dashboard id to remove this.",
    );
  }

  const expiresMs = Number(event.expiration_at_ms ?? 0);
  const expiresAt = expiresMs > 0 ? new Date(expiresMs).toISOString() : null;

  const { error, written, skipped } = await setEntitlement([appUserId], active, expiresAt);
  if (error) {
    // 500 so RevenueCat retries — a dropped grant leaves carers locked out. Only the
    // message goes to the log; the response stays generic.
    console.error(`revenuecat-webhook: update failed for ${type}: ${error.message}`);
    return json({ error: "update failed" }, 500);
  }
  if (!skipped && written === 0) {
    // Not an error, but never silent: either the account is gone, or a newer event already
    // applied and this one was correctly ignored.
    console.warn(
      `revenuecat-webhook: ${type} matched no profile row (deleted account, or superseded ` +
        `by a newer event). event_at=${eventAt}`,
    );
  }

  // After the entitlement, never before, and never able to affect the response above.
  // Anonymous ids are skipped for the same reason setEntitlement skips them: without an
  // account there is no profile to have entered a code. Those buyers are covered instead by
  // the RevenueCat subscriber attribute the client sets (see AI/design-decisions.md).
  if (ATTRIBUTING.has(type) && appUserId && !appUserId.startsWith("$RCAnonymousID") && UUID.test(appUserId))
    await recordAttribution(appUserId);

  console.log(`revenuecat-webhook: ${type} active=${active} written=${written} (${environment})`);
  return json({ ok: true, type, active, written });
});
