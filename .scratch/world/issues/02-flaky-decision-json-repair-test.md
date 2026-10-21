# Flaky: PersonaDecisionServiceTest.ShouldRespondAsync_MalformedJsonResponse_FallsBackToJsonRepair

Status: resolved

This test fails in full-suite runs and passes when run alone. It also failed on `master` before the World work. The suspect is shared static state in JsonRepairSharp, or the random Urge chaos interacting with parallel tests.

## Answer

Root cause: JsonRepairSharp keeps static parser state, so concurrent repairs in parallel tests (and in production, with several personas deciding at once) corrupted each other. All calls now go through `SafeJsonRepair` (a lock). Five full runs in a row are green.
