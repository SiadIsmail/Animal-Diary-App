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

// Entitlement ids that grant full access. Mirrors BillingConfig.EntitlementId, plus
// the same "single paid tier ⇒ any active entitlement counts" fallback the client
// applies, so a dashboard rename can never silently strip a paying owner's carers.
const ENTITLEMENT_ID = "Felova Full";

// Events after which the user HAS access. RENEWAL/UNCANCELLATION included because a
// lapse followed by a renewal must restore sponsorship immediately.
const GRANTING = new Set([
  "INITIAL_PURCHASE",
  "RENEWAL",
  "UNCANCELLATION",
  "NON_RENEWING_PURCHASE",
  "SUBSCRIPTION_EXTENDED",
  "TRANSFER",
]);

// Events after which they do NOT. CANCELLATION is deliberately absent: a cancelled
// subscription still runs to the end of its paid period, and revoking a carer's
// access early would be both wrong and unkind.
const REVOKING = new Set(["EXPIRATION", "SUBSCRIPTION_PAUSED", "REFUND"]);

function timingSafeEqual(a: string, b: string): boolean {
  const ea = new TextEncoder().encode(a);
  const eb = new TextEncoder().encode(b);
  if (ea.length !== eb.length) return false;
  let diff = 0;
  for (let i = 0; i < ea.length; i++) diff |= ea[i] ^ eb[i];
  return diff === 0;
}

Deno.serve(async (req) => {
  const expected = Deno.env.get("REVENUECAT_WEBHOOK_SECRET");
  if (!expected) {
    console.error("revenuecat-webhook: REVENUECAT_WEBHOOK_SECRET is not set");
    return new Response("not configured", { status: 500 });
  }
  if (!timingSafeEqual(req.headers.get("Authorization") ?? "", expected)) {
    return new Response("unauthorized", { status: 401 });
  }

  let event: Record<string, unknown>;
  try {
    event = (await req.json()).event ?? {};
  } catch {
    return new Response("bad json", { status: 400 });
  }

  const type = String(event.type ?? "");
  // app_user_id is the Supabase user id ONLY once the app has called Login on
  // sign-in. Anonymous ids ($RCAnonymousID:…) belong to people with no account —
  // they have no caregivers to sponsor, so there is nothing to record.
  const appUserId = String(event.app_user_id ?? "");
  if (!appUserId || appUserId.startsWith("$RCAnonymousID")) {
    return new Response(JSON.stringify({ ok: true, skipped: "anonymous" }), { status: 200 });
  }

  let active: boolean;
  if (GRANTING.has(type)) active = true;
  else if (REVOKING.has(type)) active = false;
  else {
    // CANCELLATION, BILLING_ISSUE, PRODUCT_CHANGE, TEST… — no access change.
    return new Response(JSON.stringify({ ok: true, skipped: type }), { status: 200 });
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

  const supabase = createClient(
    Deno.env.get("SUPABASE_URL")!,
    Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!,
  );

  // Update, never upsert: the profile row is created by the on_auth_user_created
  // trigger. An id with no row means the account is gone, and inserting one here
  // would resurrect billing state for a deleted user.
  const { error, count } = await supabase
    .from("profiles")
    .update(
      {
        entitlement_active: active,
        entitlement_expires_at: expiresAt,
        entitlement_updated_at: new Date().toISOString(),
      },
      { count: "exact" },
    )
    .eq("id", appUserId);

  if (error) {
    // 500 so RevenueCat retries — a dropped grant leaves carers locked out.
    console.error(`revenuecat-webhook: update failed for ${type}: ${error.message}`);
    return new Response(JSON.stringify({ error: error.message }), { status: 500 });
  }
  if (count === 0) {
    console.warn(`revenuecat-webhook: no profile for app_user_id (deleted account?)`);
  }

  return new Response(JSON.stringify({ ok: true, type, active }), { status: 200 });
});
