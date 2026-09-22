# Security

This app may read confidential database metadata and, when explicitly selected, up to three sample rows per table. Use a minimally privileged read-only database account. Keep certificate validation on for production; the untrusted-certificate switch is intended for local development only.

Sample rows are excluded from all AI requests, but schema names and custom prompts may still be sensitive. Gemini and non-loopback Ollama requests require a per-run confirmation showing the destination. Review the JSON before approval. JSON/PDF exports may contain sensitive metadata and are the user's responsibility to store and share appropriately. Gemini API keys are stored through MAUI SecureStorage and sent in the `x-goog-api-key` header.

Do not publish exploit details or credentials in an issue. Use GitHub's **Report a vulnerability** feature for this repository if available; otherwise contact the repository owner privately at [furkiak@gmail.com](mailto:furkiak@gmail.com), the address listed on the author's site. Include a minimal synthetic reproduction and affected version/commit. No response-time guarantee is asserted here.
