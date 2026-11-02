# ClickOnce Version 2 Release Readiness

## Mandatory publication gate

**PR 9 alone is not release-ready. No package or release containing the ClickOnce version 2 change may be published until PR 10 merges.** PR 10 supplies the user documentation and release-readiness guidance required for this rollout.

Local completion of PR 10 documentation is not publication authorization. It neither satisfies the merge gate nor asserts that PR 8/9's pending reviews, integration work, or bug fixes are complete. The checklist below records required release work, not completed approvals. This is a maintainer/process gate; it does not add an automated publishing block.

## Compatibility statement for release notes

The initial rollout is opt-in with `--clickonce-signing-version 2`. Omission and explicit `1` retain the legacy algorithm. No migration/deprecation warning is emitted for either version-1 selection during this rollout, and no default switch is part of this change. Only literal values `1` and `2` are accepted, and every repeated occurrence is rejected even when the values match; other signing formats are unchanged.

Version 2 follows the manifest-selected graph rather than version 1's recursive discovery. Explicit ClickOnce application `.manifest` inputs are supported, but unrelated fusion/side-by-side manifests are not claimed as ClickOnce. Full signing requires the referenced application and payloads, including excluded payloads needed for metadata refresh. SHA-256 reference hashes are refreshed via `UpdateFileInfo("v4.5")`; that argument selects hashing behavior, not the application's target framework. Legacy runtimes requiring SHA-1 manifest hashes are not newly supported.

`--no-sign-clickonce-deps` updates/signs only specified ClickOnce manifests without implicit dependency signing. `--no-update-clickonce-manifest` signs those manifests as-is without dependency resolution or file-information updates. Both require version 2, are mutually exclusive, have no short alias, and leave normal user-matched non-ClickOnce signing unchanged. The version selector also has no short alias.

The [user guide](clickonce-signing.md) covers examples and the exact combined-options diagnostic. Do not describe future warning/default proposals from the historical specification as shipped behavior.

## Caller responsibilities to communicate

- Keep publish references, mapping, and required files consistent. Audit independent glob matches in old versions; implicit traversal is not an old-version exclusion filter.
- Arrange payload/application/deployment order across invocations. Explicit application and deployment inputs within one version-2 invocation are ordered application first, including update-only and sign-as-is modes. As-is application signing can invalidate deployment references; update and re-sign the deployment afterward when needed.
- Use appropriate RSA code-signing credentials, the same certificate for related manifests, reachable/approved timestamp services, and supported Windows/.NET Framework environments. The installed package carries the required version-2 MSBuild manifest utilities and does not fall back to another MSBuild installation or cache if they are missing; this packaging decision does not remove the .NET Framework runtime prerequisite. Validate the actual deployment's installation, launch, and update behavior.
- Explicitly synchronize VSTO archival deployment copies if tooling requires parity. Version 2 preserves the VSTO-specific application-manifest XML subtree, but does not recreate the archival deployment copy. Do not assume recursive `.vsto` input matching distinguishes root deployments from archives.
- Within one invocation, overlapping requests share one owner result or failure for each coordinated original source, including nested container provenance. `--max-concurrency` bounds runnable owners and nested work; staging is not discovery authority. Do not assume coordination across invocations, transactional signing, or that `--output` assembles a complete distributable including excluded/unsupported payloads. Inspect failures and preserve a clean source artifact.

## Issue status boundary

- #681, #760, and #1065 have accepted PR 9 source, test, and extracted-package evidence for the version-2 behavior, but still require the exact final PR 10 baseline/candidate comparison before release.
- #761 remains inconclusive. Do not claim it fixed without an isolated missing-issuer test that produces a clear diagnostic instead of a generic cryptographic failure.
- #765 remains inconclusive. Do not claim it fixed without an isolated long-default-publisher activation test and a successful short explicit-publisher control.
- #842 is umbrella traceability for the proposal and specification; record any intentionally changed or unmet goal rather than treating it as one behavioral reproduction.
- #1049 is not complete until every stack exit gate, documentation claim, exact-tip package comparison, and required activation check is accepted.

## Before publishing

- [ ] PR 10 has merged, and README, package documentation, file-globbing guidance, and the user guide are present in the release source.
- [ ] PR 8/9 reviews and required bug fixes are resolved; the intended integrated commits have been reviewed. Report implementation defects to the owning PR 8 or PR 9 session rather than changing signing behavior in documentation work.
- [ ] Re-read the latest committed integration/base tips and verify the release ancestry. Revalidate documentation after parent changes; earlier test results do not cover later code.
- [ ] Build and run the repository's required suites on the exact release candidate in the required configurations, with warnings treated as errors. Confirm CLI help, strict literal and repeated-option parsing/validation, dispatch exclusivity, and unchanged non-ClickOnce handling.
- [ ] Exercise full signing, explicit application inputs, both dependency modes, reversed explicit application/deployment input order, missing application/payload diagnostics, multi-version exclusions, mapped filenames, adjacent executables, nested containers, concurrency limits, original-source/output behavior, VSTO XML-subtree preservation, and archival-copy handling. Validate certificate/timestamp behavior with approved credentials and services.
- [ ] Verify produced signatures and refreshed SHA-256 hashes/sizes/identities, then installation, launch, and update on supported ClickOnce and VSTO clients. Cover the actual release layouts, not just test fixtures.
- [ ] Compare published `sign` version `0.9.1-beta.26528.1` with candidate omitted version 1, explicit version 1, and explicit version 2 using the same unsigned fixtures. Record exact commands, versions, exit codes, diagnostics, changed files, signatures, hashes, temporary files, and cleanup.
- [ ] Record every issue check below in the release evidence table against the exact frozen candidate commit and package. Include the fixture/environment, command, result artifacts, and one disposition: fixed by version 2, not fixed by version 2, partially fixed, or inconclusive. A missing run is not an inconclusive result and blocks publication.
- [ ] For #681, sign multiple ClickOnce deployment/application pairs and multiple VSTO deployment/application pairs coexisting in one directory. Verify each deployment selects its declared application, each physical source has one signing owner/callback, all final references match shipped bytes, and no manifest ambiguity occurs.
- [ ] For #760, use multiple version directories with a deployment referencing a non-lexically-latest version. Verify implicit traversal selects only that version, then independently match another version and verify normal dispatch signs it exactly once without changing deployment selection.
- [ ] For #1065, exercise broad explicit/glob/implicit overlap for ClickOnce and VSTO with both input orders, effective concurrency 1 and parallel execution, and in-place and out-of-place output. Verify one owner/callback per physical source, deterministic publication, and final manifest hashes matching shipped bytes.
- [ ] For #761, use an issued code-signing leaf with its issuer absent from both CurrentUser and LocalMachine stores. Compare the published baseline, candidate version 1, and candidate version 2 without mutating unsigned inputs; record whether the result is fixed, not fixed, partially fixed, or inconclusive. A generic cryptographic exception is not a fixed diagnostic.
- [ ] For #765, sign without `--publisher-name` using a certificate subject long enough to stress the generated publisher/product data, inspect the resulting manifest values and diagnostics, and perform isolated ClickOnce activation for the baseline and candidate versions. Run a short explicit `--publisher-name` control that must activate, then record the disposition.
- [ ] Repack and extract the exact clean committed PR 10 tip. Repeat packaged-only Microsoft.Build provenance/isolation, VSTO preservation, missing-required-assembly no-fallback, and ordinary-format smoke checks; do not substitute an earlier PR 9 package.
- [ ] Trace #842 against the final implementation/specification goals and record intentionally changed or unmet goals. Mark #1049 complete only after every stack exit gate, documentation claim, issue disposition, exact-tip package comparison, and required activation check is accepted.
- [ ] Check documentation links, examples against actual CLI help/validation, and whitespace (`git diff --check`). Review the final diff against the current parent to separate documentation work from inherited implementation.
- [ ] Publish release notes stating opt-in compatibility boundaries and caller responsibilities. Obtain the normal release approvals; satisfying the documentation gate alone does not authorize publishing.

## Accepted PR 9 evidence is not final release approval

Accepted PR 9 commit `b8eabb7a0fae5b0a13ab154954bf5e406384de14` has exact parent PR 8 commit `9faaf9240f29b850d27228b69997202e527a89d9`. Its final Debug and Release solution validations each reported 1,883 passed, 0 failed, and 1 existing elevation-gated skip, with zero build warnings and errors. Its extracted-package gate ran 26 cases with 71 recorded proofs, including ordinary ClickOnce and VSTO signing, explicit application manifests, version-1 nonselection, multi-pair and multi-version scenarios, overlap/concurrency/output cases, both dependency modes, VSTO subtree preservation, ordinary-format smoke tests, packaged Microsoft.Build provenance, and missing-assembly no-fallback behavior.

That package was linked byte-for-byte to the accepted PR 9 source but was produced before the PR 9 commit, so its embedded revision metadata may identify the earlier base. It is evidence for PR 9 behavior, not a substitute for rebuilding, extracting, and comparing the exact clean committed PR 10 tip. The PR 9 evidence used synthetic manifests, a local untrusted self-signed certificate, and a public RFC 3161 service; it does not prove ClickOnce/Office activation, trusted-chain behavior, #761, or #765.
