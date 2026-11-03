# ClickOnce Release Readiness

This checklist is a publication gate for an exact release candidate. It is
implementation-independent and reusable across candidate revisions. Completing
documentation or individual implementation changes does not authorize
publication.

The [user guide](clickonce-signing.md) is the sole authority for current shipped
CLI options, interactions, defaults, examples, and caller guidance. The
[algorithm specification](specs/ClickOnce-Signing-Algorithm.md) records design
rationale, non-goals, coordination details, and algorithm appendices.

## External release evidence

Record results in a release evidence directory or table outside the repository
document. Before any check is accepted, record:

- the exact candidate commit and tree hashes, package version and package
  SHA-256 hash, source/package provenance, ancestry, build configuration,
  operating environment, and evidence index;
- every command, complete input fixture identity, exit code, diagnostic,
  generated or changed file, signature/hash/identity result, temporary-file
  observation, cleanup result, and retained artifact;
- one final disposition for every required issue check: fixed by version 2, not
  fixed by version 2, partially fixed, or inconclusive.

A missing run is not an inconclusive result and blocks publication. Evidence
from another commit or package does not satisfy an exact-candidate gate.

## Exact-candidate checklist

- [ ] Freeze the candidate commit and build its package from a clean checkout.
      Record their hashes, verify embedded revision/provenance, and confirm the
      intended ancestry and documentation are present. Re-run affected checks
      after any source, ancestry, build, or package change.
- [ ] Build and run the repository's required suites in every release
      configuration, with warnings treated as errors.
- [ ] Validate the extracted package's actual CLI help and parser behavior
      against the [user guide](clickonce-signing.md), including version
      selection, omission/default behavior, repeated selectors, dependency-mode
      validation and mutual exclusion, dispatch exclusivity, and unchanged
      non-ClickOnce handling.
- [ ] Compare the selected published baseline with candidate omitted-version-1,
      explicit-version-1, and version-2 runs on the same pristine unsigned
      fixtures.
- [ ] Exercise full signing, explicit application-manifest inputs, update-only
      and sign-as-is modes, reversed explicit application/deployment input
      order, missing application/payload diagnostics, multi-version exclusions,
      mapped filenames, adjacent executables, nested containers, concurrency
      limits, original-source identity, and in-place/out-of-place publication.
- [ ] Verify produced signatures and refreshed SHA-256 hashes, sizes, and
      identities. Perform installation, launch, and update checks on supported
      ClickOnce and VSTO clients using the actual release layouts, approved
      credentials, and approved timestamp services.
- [ ] Verify the user guide's operational prerequisites and caller
      responsibilities against the candidate, including RSA certificate/private
      key access, related-manifest certificate consistency, timestamp
      availability, runtime requirements, cross-invocation ordering,
      nontransactional failure handling, and incomplete `--output` publication.
- [ ] Verify VSTO application-manifest subtree preservation and the documented
      caller responsibility for any archival deployment copy. Confirm recursive
      input matching does not silently substitute for explicit root selection.
- [ ] From the extracted candidate package only, prove the required
      Microsoft.Build manifest-utility provenance and isolation. Remove a
      required packaged assembly and verify failure without fallback to Visual
      Studio, the .NET SDK, or a global cache. Repeat ordinary-format smoke
      checks.
- [ ] Apply the trusted-filesystem assumption: input/output roots, ancestor path
      components, and descendants are signer-controlled regular paths without
      junctions, symbolic links, mount points, or other reparse points, and
      lower-trust principals cannot mutate them during the invocation. Record
      that Windows file APIs may transparently follow reparse points and that
      version 2 does not enforce physical filesystem containment.

## Issue disposition matrix

- [ ] **#681:** Sign multiple ClickOnce deployment/application pairs and
      multiple VSTO deployment/application pairs coexisting in one directory.
      Verify each deployment selects its declared application, each physical
      source has one signing owner/callback, final references match shipped
      bytes, and no manifest ambiguity occurs.
- [ ] **#760:** Use multiple version directories with a deployment referencing a
      non-lexically-latest version. Verify implicit traversal selects only that
      version, then independently match another version and verify ordinary
      dispatch signs it exactly once without changing deployment selection.
- [ ] **#1065:** Exercise broad explicit/glob/implicit overlap for ClickOnce and
      VSTO with both input orders, effective concurrency 1 and parallel
      execution, and in-place and out-of-place publication. Verify one
      owner/callback per physical source, deterministic publication, and final
      manifest hashes matching shipped bytes.
- [ ] **#761:** Use an issued code-signing leaf with its issuer absent from both
      CurrentUser and LocalMachine stores. Compare the published baseline and
      candidate version-1/version-2 paths without mutating unsigned inputs. A
      generic cryptographic exception is not a fixed diagnostic; do not claim
      this issue fixed without the required evidence.
- [ ] **#765:** Sign without an explicit publisher name using a certificate
      subject long enough to stress generated publisher/product data. Inspect
      manifest values and diagnostics, perform isolated activation for baseline
      and candidate paths, and run a short explicit-publisher control that must
      activate. Do not claim this issue fixed without the required evidence.
- [ ] **#842:** Trace the final implementation against the specification's goals
      and record every intentionally changed or unmet goal rather than treating
      this as one behavioral reproduction.
- [ ] **#1049:** Mark complete only after every ancestry, build, test, CLI,
      package, documentation, issue-disposition, and activation gate is
      accepted.

## Publication checklist

- [ ] Validate all documentation links, examples against the extracted CLI, and
      whitespace with `git diff --check`.
- [ ] Publish release notes that state the opt-in compatibility boundary,
      re-signing and cross-invocation responsibilities, VSTO archival-copy
      responsibility, nontransactional/output limitations, runtime and package
      prerequisites, and the trusted-path/reparse-point limitation.
- [ ] Obtain normal release approvals and link the immutable external evidence.
      Documentation completion alone is not publication authorization.
