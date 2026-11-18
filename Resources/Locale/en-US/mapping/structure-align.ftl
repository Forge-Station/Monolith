cmd-align-hint-id = Map ID
cmd-align-hint-dry = Whether to preview without applying changes.
cmd-align-feedback-none = { $dry ->
    [true] Dry run: no alignable structures found.
   *[other] No alignable structures found.
}.
cmd-align-feedback-good = { $dry ->
    [true] Dry run: all structures are aligned.
   *[other] All structures are aligned.
}.
cmd-align-feedback = { $dry ->
    [true] Dry run: { $fixed } structures would be aligned.
   *[other] Aligned { $fixed } structures.
}.
