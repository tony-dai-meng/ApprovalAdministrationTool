# Manual tests in XrmToolBox

These checks cover what the automated suites (see [RESULTS.md](RESULTS.md)) can't: the real Dataverse environment, the
Teams Approvals API and Microsoft Graph. Each one starts with a step that changes nothing.

Tested on: ____________  By: ____________  Environment: ____________  Commit: ____________

## 1. Admin check

- [ ] Connect as yourself (a System Administrator). All buttons should work and no reason line should show.
- [ ] If you can, connect once as a user without the role. Every button should be off, with this under them:
  "You must be a System Administrator for this environment to use these functions."

Notes:

## 2. Default comment

- [ ] Tick an open approval and click **Approve**, but **cancel the window**. The comment should start
  "Approved by {your name} (administrator)…".

Notes:

## 3. Custom responses

- [ ] Select an approval with custom responses. Approve and Reject should be replaced by one **Action** button.
- [ ] Click **Action** (cancel the window afterwards). You should get a dropdown of its responses, and the send button
  should stay off until one is picked, then show the response picked.
- [ ] A custom-response approval that waits for all responses should list, in Currently With, the people who haven't
  responded yet (not N/A).
- [ ] To really test it, use an approval of your own, pick a response and send it. This is the one part Teams hasn't
  accepted yet.

Notes:

## 4. "Everyone must approve"

- [ ] One of these approvals should show **N/A** in Currently With.
- [ ] Its Approve window should list who has responded and who is still waiting.

Notes:

## 5. Remind

- [ ] Send a reminder to a colleague who's fine getting one, or a test account. You can't remind yourself.
- [ ] If you and the approver already have a one-on-one Teams chat, the reminder arrives there.
- [ ] If you don't, the tool says it emailed the approver instead. Check the email arrived (it's also in your Sent
  Items).
- [ ] If both are refused, you get one error listing each refusal, and nothing is sent.

Notes:

## 6. Borrowed rows

- Nothing to do. If an action is ever cut off partway, the next connection will offer to give the rows back.
