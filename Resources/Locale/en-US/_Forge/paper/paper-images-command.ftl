cmd-paperimages-desc = Lists papers with uploaded images, or opens one for visual review.
cmd-paperimages-help = Usage: paperimages
    Lists every paper currently in the round that contains a [px] drawing.
    Usage: paperimages <netEntity>
    Opens that paper in read-only mode so you can inspect the image. There is no automatic detection of prohibited pictures — look at them yourself.
    Then: follow <netEntity>  or  tpto <netEntity>
cmd-paperimages-hint = <paper netEntity>
cmd-paperimages-header = Papers with uploaded images:
cmd-paperimages-footer = {$count} paper(s). Open one with: paperimages <netEntity>
cmd-paperimages-none = No papers with uploaded images found this round.
cmd-paperimages-entry = {$entity}  net={$net}  {$count} image(s) ({$sizes})  {$location}  preview: {$preview}
cmd-paperimages-held-by = held by {$holder}
cmd-paperimages-at = at {$coords}
cmd-paperimages-invalid-entity = Could not parse entity '{$value}'. Expected a NetEntity like n123.
cmd-paperimages-not-paper = {$entity} is not paper.
cmd-paperimages-no-images = That paper has no uploaded images, opening it anyway.
cmd-paperimages-open-failed = Failed to open {$entity}.
cmd-paperimages-opened = Opened {$entity} ({$count} image(s): {$sizes}).
