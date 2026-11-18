# Trusted release signing

## Current status

The existing v0.1 release files are **unsigned**. No signing identity is currently configured. The release workflow is prepared to use Microsoft Artifact Signing, but a successful real signing run cannot happen until the account, validated identity, certificate profile, and GitHub federation described below exist.

New publication attempts require trusted signing configuration and stop before replacing release assets if setup or signature verification fails. Local builds and explicitly requested build-only CI artifacts remain unsigned and are not uploaded as GitHub release downloads.

Checksums detect changes to downloaded files; they do not identify a trusted publisher. Self-signed certificates do not establish public publisher trust. Even a valid public signature does not guarantee that a new application will immediately stop showing reputation warnings.

## One-time setup by the publisher

1. Use an Azure subscription and an eligible identity to create a **Microsoft Artifact Signing** account. Review the service's terms, regional eligibility, and pricing before purchasing it.
2. Complete identity validation in the Azure portal and create a **Public Trust** certificate profile. This is required for publicly distributed downloads; a Private Trust profile is not a replacement.
3. Create an Entra application/service principal for GitHub Actions. Grant it only the **Artifact Signing Certificate Profile Signer** role at the intended certificate-profile scope.
4. Add a federated GitHub credential with issuer `https://token.actions.githubusercontent.com`, audience `api://AzureADTokenExchange`, and subject `repo:santa512/AutoTyper:ref:refs/heads/main`.
5. In GitHub **Settings → Secrets and variables → Actions → Variables**, configure these repository variables:

| Variable | Value |
| --- | --- |
| `AZURE_CLIENT_ID` | Application/client ID of the signing service principal. |
| `AZURE_TENANT_ID` | Microsoft Entra directory ID. |
| `AZURE_SUBSCRIPTION_ID` | Subscription that contains the signing account. |
| `ARTIFACT_SIGNING_ENDPOINT` | The signing account's region endpoint, such as `https://eus.codesigning.azure.net/`. |
| `ARTIFACT_SIGNING_ACCOUNT` | Signing account name. |
| `ARTIFACT_SIGNING_PROFILE` | Validated Public Trust certificate-profile name. |

These values are service identifiers, not private keys. Authentication uses GitHub OIDC; no Azure client password, PFX file, certificate password, or private key is placed in this repository. Do not paste private credentials into chat or commit them to source control.

The initial federated credential permits only the `main` branch. To sign from a release tag, add a separate federated credential restricted to that specific tag (for example `repo:santa512/AutoTyper:ref:refs/tags/v0.2`). Keep branch review requirements in place.

## Publish signed files after setup

From the GitHub Actions page, run **Build Auto Typer release** on `main` with:

- `tag`: the existing release tag to update, such as `v0.1`.
- `edition`: `both`, `winui`, or `compact`.
- `publish`: `true`.

The workflow builds the selected editions, copies only their EXEs into a staging folder, authenticates with Azure, signs and timestamps every EXE, checks Windows reports `Valid` and a timestamp certificate, then calculates checksums from the **signed** bytes. Only then does it upload the files and update the release notes. It does not fall back to unsigned release uploads.

Signing can be performed without building by using the service's official tools, but any manual re-signing must also regenerate the `.sha256` file and verify the final signature before release upload.

## Build and test without a signing account

Run the same workflow with `publish=false`. It builds and tests without contacting Azure and uploads an explicitly named **AutoTyper-unsigned-test-builds** Actions artifact. It does not create or modify a GitHub Release.

## Verify a published download

On Windows:

```powershell
Get-AuthenticodeSignature .\AutoTyper.exe |
    Format-List Status, StatusMessage, SignerCertificate, TimeStamperCertificate
Get-FileHash .\AutoTyper.exe -Algorithm SHA256
```

Use `AutoTyperCompact.exe` for the Compact version. Confirm the signature reports `Valid`, inspect the publisher identity, and compare the SHA-256 with the release checksum. The existing unsigned v0.1 files will continue to report `NotSigned` until a signed rebuild replaces them.

## Official references

- [Set up Artifact Signing](https://learn.microsoft.com/en-us/azure/artifact-signing/quickstart)
- [Artifact Signing GitHub Action](https://github.com/Azure/artifact-signing-action)
- [SmartScreen reputation for Windows developers](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation)
