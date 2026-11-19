# AI summaries (v0.3, main x64)

Open **Settings → AI summary** and save a **primary Groq API key**, with an optional **secondary key**. Keys are encrypted for your Windows account and included in password-encrypted settings exports. Clearing both disables the summarize icons. Manage keys at the [Groq console](https://console.groq.com/keys).

The summarize icon sits beside Translate in the main window and caption overlay:

- **Main window:** summarizes an immutable copy of the input text into a separate expandable side panel. The input stays visible and editable. It is never replaced by the summary.
- **Captions:** summarizes captured source-labeled speech, including the current partial phrase, into its own expandable panel. Captions stay visible and continue updating. Summarizing does not advance the translation cursor, and translating does not remove summary context.

The selected translation language is also used for the summary. Main input accepts up to 60,000 characters per summary. Long caption sessions retain the most recent 60,000 characters at segment boundaries and label that panel **Recent captions**. Each click takes a new snapshot; no summary request is sent automatically. Close a summary panel to cancel its pending request.

Requests use Groq's [Chat Completions API](https://console.groq.com/docs/text-chat) with the production [`openai/gpt-oss-20b` model](https://console.groq.com/docs/models). Only source text is sent when the summarize icon is clicked; audio is not uploaded. A 429 quota/rate-limit or 402 quota/payment response retries once with the distinct secondary key. Other errors are shown directly; there are at most two attempts per click, and cancellation prevents failover. If only the secondary key is filled, it is used directly.

Prior summaries remain visible if a later request fails. Editing input during a request does not change that request; the panel indicates when its result belongs to an older input snapshot.

`tests/Summary.Tests` uses mocked HTTP responses to check authorization, request content, language, errors, cancellation, two-key failover, Windows encryption and independent caption context. The isolated UI harness uses deterministic local responses and does not persist preferences or make Groq calls.
