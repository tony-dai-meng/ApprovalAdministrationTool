# Test results

<!-- Written by Tests/run-tests.ps1 on 2026-11-28 18:10:09 +11:00. Run it again to refresh. -->

Last run: **2026-11-28 18:10:09 +11:00** on the `sequential` changes before they were committed (against a copy of the build, as XrmToolBox had the plugin open), Debug build, took 31 s.

All 17 suites passed (372 checks).

| Suite | Result | Checks passed | Checks failed | Notes |
| --- | --- | ---: | ---: | --- |
| ActionsTest | passed | 37 | 0 |  |
| ButtonsShot | passed | 10 | 0 |  |
| DelegatedTest | passed | 11 | 0 |  |
| DragTest | passed | 8 | 0 |  |
| FilterTest | passed | 28 | 0 |  |
| FlowTest | passed | 20 | 0 |  |
| FriendlyTest | passed | 23 | 0 |  |
| GridTest | passed | 10 | 0 |  |
| NewFeaturesTest | passed | 47 | 0 |  |
| OwnerSwapTest | passed | 7 | 0 |  |
| QueryTest | passed | 38 | 0 |  |
| ReassignUiTest | passed | 23 | 0 |  |
| SelectionTest | passed | 23 | 0 |  |
| SeqReassignTest | passed | 12 | 0 |  |
| ServiceHarness | passed | 37 | 0 |  |
| SyncTest | passed | 31 | 0 |  |
| TooltipTest | passed | 7 | 0 |  |

Each suite is a console program in this folder. It loads the built plugin by reflection and checks it against in-memory fakes of Dataverse and the Teams Approvals API, so no environment is touched. See run-tests.ps1.
