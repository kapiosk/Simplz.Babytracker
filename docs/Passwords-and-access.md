# Passwords and access

There are no user accounts. There are two passwords, and which one you type decides what you can do.

| Password | What it gives |
| --- | --- |
| **Parent** | Everything: logging, editing, deleting, attaching photos, adding babies. Lands on **Track**. |
| **Doctor** | The same **Track**, **Chart** and **Report**, with every button that writes removed. Lands on **Chart**. |

Read-only means read-only. It is not a matter of hiding buttons — the pages are built on the server,
so a read-only visitor's page has no editing in it at all, and every write checks the role again
before touching anything.

The doctor password can still switch between babies, look at photos, and set the theme. None of
those change any records.

## Changing them

**Babies → Change the passwords.** It asks for the current parent password first, so an unlocked
phone left on a table is not by itself enough to lock everybody else out.

Either password can be changed on its own; leaving a box empty keeps that one as it is. Passwords
set here are stored scrambled, not as text, and they override whatever the server was started with.

## The thing to know about signing out

**Changing a password does not sign anyone out.** A phone that is already signed in stays signed in
for months whatever the password becomes. That is what you want at three in the morning, and not at
all what you want if you are changing it because somebody has it who should not.

For that case there is **Sign out every device**, which does exactly that — including the phone you
are holding, which will ask you to sign in again immediately. Tick it when the point of changing the
password is to lock somebody out.

## Staying signed in

The sign-in lasts six months and renews itself as you use it, so a phone with the app on its home
screen is not forever asking. Restarting or rebuilding the server does not sign anybody out.
