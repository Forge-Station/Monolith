# Wolfgate admin internet sounds

## Radio popup
wf-internet-sound-popup-title = Admin Radio
wf-internet-sound-popup-played = Played by {$admin}
wf-internet-sound-popup-played-stealth = Now playing
wf-internet-sound-popup-loading = Tuning in... (from {$admin})
wf-internet-sound-popup-loading-stealth = Tuning in...
wf-internet-sound-popup-volume = Volume
wf-internet-sound-popup-stop = Mute

## Admin window
wf-admin-tab-internet-sound = Play Internet Sound
wf-internet-sound-admin-title = Play Internet Sound
wf-internet-sound-admin-info = Plays a YouTube, SoundCloud or other link to every connected player, or out of one ship's PA speakers.
wf-internet-sound-admin-url = https://...
wf-internet-sound-admin-play = Play
wf-internet-sound-admin-stealth-play = Stealth Play
wf-internet-sound-admin-stealth-play-tooltip = Plays without showing anyone, other admins included, who played it. The admin logs still name you.
wf-internet-sound-admin-stealth-requester = an unnamed admin
wf-internet-sound-admin-stop = Stop for everyone
wf-internet-sound-admin-pa = Over ship PA
wf-internet-sound-admin-grid = Grid entity id
wf-internet-sound-admin-pa-hint = The track plays from the ship's speakers, so it carries, fades with distance and distorts with damage.
wf-internet-sound-admin-need-grid = Enter the grid entity id of the ship to play it on.

## Status messages
wf-internet-sound-server-name = The server
wf-internet-sound-disabled = Internet sounds are disabled on this server.
wf-internet-sound-invalid-url = Use an HTTPS link on the standard port (443).
wf-internet-sound-fetching = Fetching {$url}...
wf-internet-sound-sending = Sending "{$title}" ({$size} MB) to {$count} players. It starts once they all have it.
wf-internet-sound-playing = Playing "{$title}" to everyone.
wf-internet-sound-playing-pa = Playing "{$title}" over the PA of {$ship}.
wf-internet-sound-stopped = Internet sound stopped for everyone.
wf-internet-sound-busy = "{$title}" is still playing. Stop it before playing something else.
wf-internet-sound-busy-fetching = A sound is still being fetched. Stop it before playing something else.
wf-internet-sound-busy-sending = "{$title}" is still being sent out. Stop it before playing something else.
wf-internet-sound-no-ship = That isn't a ship, or it's already gone.
wf-internet-sound-no-source = The speaker is gone.
wf-internet-sound-error-playlist-empty = Couldn't read that playlist. {$detail}
wf-internet-sound-no-speakers = {$ship} has no working PA speakers.
wf-internet-sound-too-many = Too many sounds are already in play. Wait for one to finish.
wf-internet-sound-admin-entry = "{$title}" from {$admin}
wf-internet-sound-admin-entry-pa = {$ship}: "{$title}" from {$admin}
wf-internet-sound-admin-fetching = Fetching a sound...
wf-internet-sound-admin-sending = Sending the sound to players...
wf-internet-sound-admin-idle = Nothing playing.
wf-internet-sound-error-ytdlp-missing = yt-dlp wasn't found ({$detail}). Install it or set wf.internet_sound.ytdlp_path.
wf-internet-sound-error-ffmpeg-missing = ffmpeg wasn't found ({$detail}). Install it or set wf.internet_sound.ffmpeg_path.
wf-internet-sound-error-download = yt-dlp couldn't fetch that link: {$detail}
wf-internet-sound-error-download-too-large = The source download exceeded the {$detail} MB input limit.
wf-internet-sound-error-host = {$detail} is a private, local or unresolvable address, which isn't allowed.
wf-internet-sound-error-https = The link and all download destinations must use HTTPS on port 443. Unencrypted redirects and streams are not allowed.
wf-internet-sound-error-rejected = That link is a livestream or longer than the {$detail} second limit.
wf-internet-sound-error-transcode = ffmpeg couldn't convert the audio: {$detail}. It needs libvorbis support.
wf-internet-sound-error-too-large = The converted audio is {$detail} MB, over the size limit.
wf-internet-sound-error-unplayable = The converted audio came out in a form the game can't play. Check the server log.
wf-internet-sound-error-timeout = Fetching took too long and was cancelled.
wf-internet-sound-error-unknown = Something went wrong fetching the sound. Check the server log.

## Commands
cmd-playinternetsound-desc = Plays a YouTube, SoundCloud or other yt-dlp supported link to every connected player.
cmd-playinternetsound-help = Usage: {$command} <link> [stealth]
    With stealth true, nobody is shown who played it; only the admin logs record it.
cmd-playinternetsound-hint = <link>
cmd-playinternetsound-hint-stealth = [stealth]
cmd-playinternetsound-invalid-args = Expected a link and an optional stealth flag.
cmd-playinternetsound-invalid-stealth = {$value} isn't true or false.
cmd-playinternetsoundpa-desc = Plays a yt-dlp supported link out of one ship's PA speakers.
cmd-playinternetsoundpa-help = Usage: {$command} <grid> <link> [stealth]
cmd-playinternetsoundpa-hint-grid = <grid>
cmd-playinternetsoundpa-invalid-args = Expected a grid entity id, a link and an optional stealth flag.
cmd-playinternetsoundpa-invalid-grid = {$grid} isn't a grid.
cmd-stopinternetsound-desc = Stops the current internet sound for everyone.
cmd-stopinternetsound-help = Usage: {$command}
