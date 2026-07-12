# Thoth Data Tools

This folder is reserved for local-only dataset transport and extraction utilities.

- Do not call hosted model APIs from these tools.
- Do not upload repository files, prompts, conversations, or datasets.
- Respect the download approval gate before acquiring any external source.
- Keep raw downloads and extracted corpora under ignored `data/raw`, `data/staging`, or `data/normalized` directories.
