# Installing it on a phone

Open the app in the phone's browser and use **Add to home screen**. It then runs full screen with
its own icon and behaves like any other app — which matters at 3am, because it means no address bar
to fumble past.

- **iPhone:** Safari → Share → *Add to Home Screen*
- **Android:** Chrome → menu → *Add to Home screen* or *Install app*

## It needs HTTPS

Phones only offer installation on `https://` addresses. If the app is reached by a plain
`http://` address or a bare IP, the option will not appear.

That is worth fixing for more than the icon: over plain HTTP the password, the entries and any
photographs cross the network in the clear.

## It is not an offline app

Entries are written on the server, so logging needs the phone to be able to reach it. Out of range,
you get a *you're offline* page rather than a browser error, and it picks up again when the
connection comes back.

In practice this means it works anywhere in the house, and does not work in the car.

## Light or dark

**Babies → Appearance** offers light, dark, or following whatever the phone is set to, which is the
default. It is remembered per device, so the phone used at night can be dark while the other stays
light.
