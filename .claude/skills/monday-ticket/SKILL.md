---
name: monday-ticket
description: Pull the monday.com ticket matching the TaskID (e.g. TJKG-014) parsed from the current git branch name, from the ShopFlow board. Use when the user asks to look up, pull, fetch, or show "the ticket"/"the monday ticket"/"the task" for the current branch.
---

# Monday Ticket Lookup

Fetches the monday.com item corresponding to the TaskID embedded in the current branch name.

Board URL and ID are read from the `MONDAY_BOARD_URL` and `MONDAY_BOARD_ID` variables in the project's `.env` file (see `.env.example` for the expected format).

## Steps

1. **Load the board config.**
   - Read `MONDAY_BOARD_URL` and `MONDAY_BOARD_ID` from the project's `.env` file.
   - If either variable is missing, tell the user to add them to `.env` (see `.env.example`) and stop — don't guess a board ID.

2. **Determine the TaskID.**
   - If the user passed an explicit TaskID or branch name as an argument, use that instead of the current branch.
   - Otherwise run `git branch --show-current` to get the branch name.
   - Extract the TaskID with the pattern `[A-Za-z]{2,}-[0-9]+` (matches `TJKG-014`, `dev/TJKG-013`, `dev/TJKG-011-UI`, etc. — the branch may have a `dev/` prefix and/or a trailing `-slug`).
   - If no match is found, tell the user the branch name doesn't contain a recognizable TaskID and ask them for one — don't guess.

3. **Check monday.com access.**
   - Use `ToolSearch` with query `"monday"` to load the monday.com MCP tools.
   - If the monday.com connector is not authorized (no tools resolve, or a tool call fails with an auth error), stop and tell the user to authorize the monday.com connector via their claude.ai connector settings — do not ask them for tokens or attempt a workaround.

4. **Find the item.**
   - Query board `MONDAY_BOARD_ID` and look for an item whose **name** contains the TaskID string (case-insensitive substring match, e.g. an item named "TJKG-014: Add vendor role support" matches TaskID `TJKG-014`).
   - If exactly one item matches, proceed to step 5.
   - If multiple items match, list them (name + item ID) and ask the user which one they meant.
   - If none match, tell the user no item on this board matches that TaskID — suggest double-checking the TaskID or the board.

5. **Present the ticket.**
   - Fetch the full item details: status/column values, assignee(s), description or latest updates, and the item's monday.com URL.
   - Summarize concisely for the user (status, assignee, key details) and include the direct link to the item.
