import assert from "node:assert/strict";

const required = ["TESTMAIL_APIKEY", "TESTMAIL_NAMESPACE", "TESTMAIL_TAG", "TEST_STARTED_AT_MS"];
for (const name of required) {
  assert.ok(process.env[name], `${name} is required`);
}

const endpoint = new URL("https://api.testmail.app/api/json");
endpoint.searchParams.set("apikey", process.env.TESTMAIL_APIKEY);
endpoint.searchParams.set("namespace", process.env.TESTMAIL_NAMESPACE);
endpoint.searchParams.set("tag", process.env.TESTMAIL_TAG);
endpoint.searchParams.set("timestamp_from", process.env.TEST_STARTED_AT_MS);
endpoint.searchParams.set("livequery", "true");

const response = await fetch(endpoint, {
  headers: { accept: "application/json" },
  redirect: "follow",
  signal: AbortSignal.timeout(300_000),
});
assert.equal(response.ok, true, `testmail returned ${response.status}`);
const inbox = await response.json();
assert.equal(inbox.result, "success", inbox.message ?? "testmail query failed");
assert.ok(inbox.emails?.length > 0, "no contract email arrived");

const email = inbox.emails[0];
assert.match(email.subject ?? "", /^Test intelligence: 1 material update$/);
assert.match(email.html ?? "", /Stella/);
assert.match(email.html ?? "", /Cosmic Digest/);
assert.match(email.html ?? "", /Test&#39;s Intelligence Brief/);
assert.match(email.html ?? "", /Cosmic Contract Test event/);
assert.doesNotMatch(email.html ?? "", /testmail-contract-v1/);
assert.match(email.text ?? "", /What changed:/);
assert.match(email.text ?? "", /No quota filling/);

console.log(`Verified Testmail delivery ${email.id ?? "unknown"}: ${email.subject}`);
