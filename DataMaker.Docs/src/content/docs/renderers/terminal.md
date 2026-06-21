---
title: Terminal renderer
description: Fill and submit a DataMaker form in your terminal from a .dmf file or URL.
---

The terminal renderer (`datamaker-form`) is a TUI client: point it at a `.dmf`
(file or URL), fill the form in your terminal, and it seals + submits — the same
end-to-end-encrypted path as every other client. It's a self-contained,
single-file binary with no runtime to install, so it's handy for SSH sessions,
scripts, and headless boxes where a browser isn't an option.

## Download

Grab the binary for your platform from the
[**GitHub Releases**](https://github.com/FOBO-Tools/fobo-data-maker/releases)
page:

| Platform | Asset |
|---|---|
| macOS (Apple Silicon) | `datamaker-form-osx-arm64` |
| macOS (Intel) | `datamaker-form-osx-x64` |
| Linux (x86-64) | `datamaker-form-linux-x64` |
| Linux (ARM64) | `datamaker-form-linux-arm64` |
| Windows (x64) | `datamaker-form-win-x64.exe` |

On macOS/Linux, make it executable (and optionally drop it on your `PATH`):

```sh
chmod +x datamaker-form-osx-arm64
mv datamaker-form-osx-arm64 /usr/local/bin/datamaker-form
```

:::tip
First launch on macOS may be blocked by Gatekeeper — clear the quarantine flag
with `xattr -d com.apple.quarantine datamaker-form` (or right-click → Open once).
:::

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

## Build from source

Prefer to build it yourself? A self-contained single-file binary is produced per
platform (osx-arm64, osx-x64, linux-x64, linux-arm64, win-x64), no runtime
required:

```sh
dotnet publish DataMaker.Forms.Renderer.Terminal/DataMaker.Forms.Renderer.Terminal.csproj \
  -c Release -r osx-arm64 --self-contained -o out
# -> out/datamaker-form
```

Swap `-r` for your target runtime identifier. The published Releases are built
the same way, one per RID.
