#!/usr/bin/env bash
#
# Deploying, with the check that is easy to get wrong.
#
# Whether a phone shows the "what's new" notice is decided entirely by the version string at the
# top of Services/Changelog.cs. Write the release notes, forget to change that string, and nobody
# is ever told: the notes sit in the build, correct and unread, and nothing anywhere complains.
# So this refuses to deploy a version that is already running unless told that is deliberate.
#
# It also carries the compose project name, which has to be simplzbabytracker. A different one
# silently creates a new empty volume and the app comes up with no data in it.
#
#   scripts/deploy.sh                  deploy, refusing if the version has not moved
#   scripts/deploy.sh --same-version   deploy anyway, knowing nobody will be told
#
# Override the target with DEPLOY_CONTEXT, DEPLOY_PROJECT and DEPLOY_CONTAINER.

set -euo pipefail

CONTEXT="${DEPLOY_CONTEXT:-raspberrypi5}"
PROJECT="${DEPLOY_PROJECT:-simplzbabytracker}"
CONTAINER="${DEPLOY_CONTAINER:-babytracker}"

cd "$(dirname "${BASH_SOURCE[0]}")/.."

allow_same_version=false
for arg in "$@"; do
    case "$arg" in
        --same-version) allow_same_version=true ;;
        -h|--help) sed -n '2,20p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0 ;;
        *) echo "unknown argument: $arg" >&2; exit 2 ;;
    esac
done

# The version about to go out is the first one listed in the changelog.
about_to_deploy="$(sed -n 's/.*new("\([^"]*\)".*/\1/p' Services/Changelog.cs | head -1)"

if [ -z "$about_to_deploy" ]; then
    echo "could not read a version from Services/Changelog.cs" >&2
    exit 1
fi

# Asked from inside the container, so this needs no address and no credentials.
version_now() {
    docker --context "$CONTEXT" exec "$CONTAINER" curl -fsS http://localhost:8080/healthz 2>/dev/null \
        | grep -o '"version":"[^"]*"' | cut -d'"' -f4 || true
}

running="$(version_now)"

echo "running:   ${running:-nothing yet}"
echo "deploying: $about_to_deploy"
echo

if [ -n "$running" ] && [ "$running" = "$about_to_deploy" ]; then
    if [ "$allow_same_version" = true ]; then
        echo "Same version on purpose: no notice will appear on anybody's phone."
        echo
    else
        {
            echo "Version $about_to_deploy is already running."
            echo
            echo "Add an entry at the top of Services/Changelog.cs so people are told what changed,"
            echo "or pass --same-version if this deploy genuinely has nothing worth mentioning."
        } >&2
        exit 1
    fi
fi

docker --context "$CONTEXT" compose -p "$PROJECT" up -d --build

echo
echo -n "waiting for it to answer"

for _ in $(seq 1 30); do
    now="$(version_now)"
    if [ -n "$now" ]; then
        echo
        echo "up, running $now"
        exit 0
    fi
    echo -n "."
    sleep 2
done

echo
echo "it never answered — try: docker --context $CONTEXT logs $CONTAINER" >&2
exit 1
