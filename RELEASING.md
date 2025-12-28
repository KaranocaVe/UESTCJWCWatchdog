# Releasing

This repo uses GitHub Actions to build and publish cross-platform binaries.

## Create a release

1. Create and push a tag (recommended format: `vX.Y.Z`)
   - `git tag v0.1.0`
   - `git push origin v0.1.0`
2. GitHub Actions workflow `Release` will:
   - build self-contained, single-file binaries for Windows/macOS/Linux
   - upload artifacts to the GitHub Release of that tag

## Artifacts

Each release uploads both:

- `UESTCJWCWatchdog-App-<version>-<rid>.(zip|tar.gz)`
- `UESTCJWCWatchdog-Cli-<version>-<rid>.(zip|tar.gz)`

And checksum files:

- `sha256sums-<rid>.txt` (per target)
- `SHA256SUMS.txt` (combined)
