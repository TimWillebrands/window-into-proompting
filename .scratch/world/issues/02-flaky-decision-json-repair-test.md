# Flaky: PersonaDecisionServiceTest.ShouldRespondAsync_MalformedJsonResponse_FallsBackToJsonRepair

Status: ready-for-agent

This test fails in full-suite runs and passes when run alone. It also failed on `master` before the World work. The suspect is shared static state in JsonRepairSharp, or the random Urge chaos interacting with parallel tests.
