# Verify the Jev adapter against the real API

Status: ready-for-human

`JevJudge` was written from TypeSafe's docs; there was no key to test it with. Set `TYPESAFE_API_KEY`, run the world, and check:

- the logs show `Capture gate p(memorable)=…` lines;
- Life choices look sane.

Choice keys are the option numbers ("0".."10").
