#!/bin/sh
# Compatibility entry point. All in-world behavior belongs to Kukolony's benchmark controller.
exec "$(CDPATH= cd -- "$(dirname "$0")" && pwd)/in-game-test.sh" "$@"
