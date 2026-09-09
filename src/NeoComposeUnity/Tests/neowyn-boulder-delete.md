# Neowyn boulder deletion fixture

`neowyn-boulder-delete.json.gz` was generated from Neo Compose's committed
`scripts/fixtures/neowyn-all-partitions-audit.json.gz` on September 9, 2026.
The compressed source's SHA-256 is
`23e2ef7a0129e54deed8536a234e2df83187ac80071966b8cd49adbe91d1bc7d`.

Generation expands packed rows, evaluates the real `Full` boulder variant with
`evaluateVariantPreview`, and adds its 26 constructed rows to the document.
This is a newly constructed instance of captured content, not a captured placed
boulder. Alternate constructed rows receive the test partition key
`sdk-boulder-delete`, giving 13 main rows and 13 partition rows. Their IDs and
contents otherwise come directly from construction.

`collectOwnedValueDeletionIds` supplies the tombstone IDs. `beforeExport` passes
through `buildUnityExport` and `toProjectUnityExportWire` and has 4,895 rows.
Generation also independently exported the document after deleting the graph,
confirming all 4,869 retained rows and all non-value collections were unchanged.
Only the before export is stored. The test derives the fake full-export response
by cloning it and removing the complete tombstone set. The complete schema and retained
rows allow the test to load and validate the result with `NeoProjectStore` and
deserialize its retained world partition with the SDK's typed value converters.

The test runs two transport cases against these same rows. With the actual file
manifest, deleted sprite references deliberately force a full export. The
data-only case removes only the file manifest before synchronization, allowing
the SDK to apply the same complete tombstone set incrementally. It does not
claim the real file-bearing boulder takes the incremental path.

The full-export transport uses fake download bytes and fake asset services.
It does not download or validate textures. The test starts a full SDK client
from both the untouched fixture and the synchronized result. This covers sparse
constructor replay through the captured time model's computed Sprite default,
in addition to the project-store row checks.

Run the focused cases from the SDK checkout:

```sh
unity test samples/HelloWorld --mode EditMode --filter NeoCompose.Tests.NeoComposeEditorTests.Synchronizer_DeletesCapturedBoulderAndPreservesSharedRows --output /tmp/issue-762-sdk-delete.xml
```
