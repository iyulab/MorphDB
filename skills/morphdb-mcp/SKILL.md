---
name: morphdb-mcp
description: How to read a MorphDB project through its read-only MCP door - find the tables and views, read rows and aggregates, and read the answers correctly (what the caller cannot see is absent, not missing). Use when an agent has the MorphDB MCP tools (describe_tables, describe_table, query_records, get_record, aggregate, describe_views, read_view) and has to answer a question from the data.
---

# Reading MorphDB through the MCP door

The door is read-only and runs **as the caller whose credential it was given**. Every tool says so in its description; this
skill is about reading the answers well.

## Work in this order

1. `describe_tables` - what tables exist. `describe_views` - what views exist (a view is a stored query; a materialized view is
   a stored copy and its result says when it was last refreshed - quote that date when you rely on it).
2. `describe_table` for the table you will read: column names, types, which are required. Use the names it gives; do not guess a column.
3. Read: `query_records` for rows (`select`, `filter` as `column:operator:value`, `orderBy`, `search`, `page`, `pageSize`),
   `aggregate` for counts, sums, averages and groups, `get_record` for one row by its `_id`, `read_view` for a view.
   The operators a filter takes are listed in the service's `docs/API.md`.

## Read the answers

- **Rows the caller's row-level rules hide are not in any answer.** Not in a page, not in a count or a sum, and `get_record`
  reports a hidden record as *not found*. "No rows" and "not found" therefore mean *none this caller may see*, never *none exist*.
  Do not tell a person a record does not exist; tell them what you could and could not see.
- **Pages are capped.** If `hasNext` is true there is more; ask for the next `page` before you summarize. A count taken from the
  page length is wrong when `hasNext` is true - use `aggregate` for a count.
- **Aggregates cap their groups.** If you grouped and hit `maxGroups`, say the list is partial.
- Values are what is stored. A column named `_created_by`, `_updated_by`, `_owner_id` holds the token subject of whoever wrote
  the row, as text (in tables made by this version; an older table keeps a `uuid` there until it is migrated).

## What you cannot do, and should not try

- Nothing here writes. If the task needs a change, say so and stop.
- Do not look for a way around a hidden row (other tables, views over the same rows): a view reads through the same rules.

## For whoever configures the agent

Give the agent a **person's access token or an issued secret**, never the master secret: only the master secret bypasses
row-level security, and an agent holding it reads every row. See "What the service trusts" in `docs/API.md`.
