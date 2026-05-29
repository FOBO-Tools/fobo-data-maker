---
title: Terminal renderer
description: Fill and submit a DataMaker form in your terminal from a .dmf file or URL.
---

The terminal renderer (`datamaker-form`) is a TUI client: point it at a `.dmf`
(file or URL), fill the form in your terminal, and it seals + submits — the same
end-to-end-encrypted path as every other client.

## Run

```sh
datamaker-form ./contact.dmf
datamaker-form https://example.com/forms/contact.dmf
```

On launch it:

1. Loads + **verifies** the bundle (Ed25519 signature + hashes).
2. Shows a **trust-on-first-use** prompt with the signer's fingerprint and
   identity (and whether it's FOBO-attested), so you can eyeball a new signer
   before trusting them.
3. If the form's policy is `Authenticated`, runs an OAuth sign-in.
4. Renders the fields; on submit, seals the values against the form's recipient
   public key and POSTs to the submissions endpoint.

## Options

| Flag | Purpose |
|---|---|
| `<path-or-url>` | the `.dmf` to fill (positional) |
| `--submit-endpoint <url>` | override the submissions API base |
| `--trust` | trust handling for the TOFU prompt |
| `--template` | terminal color template |

The submit path uses the [.NET SDK](/fobo-data-maker/sdks/dotnet/) (`DataMakerClient`) under the
hood, so the terminal ships only the client subset — no signing/keygen code.

## Build

A self-contained single-file binary is produced per platform (osx-arm64,
osx-x64, linux-x64, linux-arm64, win-x64). It needs no runtime installed.
