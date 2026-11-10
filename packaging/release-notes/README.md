# Release notes

One file per release, named after the version without the v: `2.0.0.md` for the tag `v2.0.0`.

`.github/workflows/release.yml` puts the file on the release it drafts, and `Build-TheRelease.ps1` refuses to build a release whose notes are missing or empty. That is on purpose. GitHub would otherwise write the notes itself from pull request titles, and the pull requests that made 2.0 are in a private repository, so every link in them would lead nowhere.

Write them for somebody deciding whether to download, not for somebody reading the code.
