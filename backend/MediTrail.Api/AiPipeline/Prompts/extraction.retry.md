Your previous response could not be parsed as JSON matching the required schema.

Parser error:
{{ERROR}}

Return the same extraction again, corrected. Requirements:

- The response starts with `{` and ends with `}`. Nothing before, nothing after.
- No markdown code fences, no explanation, no apology.
- Every property from the schema is present. Sections that do not apply are `[]`.
- Numbers are bare JSON numbers — `500`, not `"500"` or `500mg`. Units go in their own field.
- `null` is unquoted, not the string `"null"`.
- Strings use double quotes, and any quote inside a string is escaped.
- No trailing commas.

Do not change what you read from the document. Only fix the JSON.
