# When something goes wrong

The app talks to the server over a live connection. A phone that has been asleep, or has wandered
between wifi and mobile data, can come back to a page that looks perfectly normal but has quietly
lost that connection — buttons that do nothing, with no error to explain it.

Most of this is handled without you.

## The Reload button

The **↻** in the header reloads the page. It is the answer to almost anything odd, and it is safe:
nothing is lost by reloading, because a feed or a sleep that is running lives on the server rather
than in the page. A timer that is counting keeps counting.

It is deliberately not part of the app's normal machinery, so it still works when the rest of the
page does not.

## What happens on its own

- The server sends a heartbeat every few seconds. If those stop, the page rejoins the server, or
  reloads itself if it cannot.
- If a red bar appears saying the connection was lost, it is already trying.
- If the app ever shows *An unhandled error has occurred*, it reloads itself a couple of seconds
  later. If that same error comes straight back, it stops trying and leaves the message up with its
  own Reload link, rather than reloading in circles.

## Being told about updates

After the server is updated, the next phone to open the app gets a line under the header — *Updated
to 1.8 · What's new* — leading to a page listing what changed in each version, and when the running
copy was last updated.

Reading or dismissing it is per device, so one parent reading it does not make it vanish for the
other.

## If something is genuinely broken

Whoever runs the server can see what the app reported. Failures in the page are sent back and
recorded, with how long the phone had been away beforehand, which is usually enough to say what
happened without anybody having to reproduce it.
