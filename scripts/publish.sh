#!/bin/sh

target="Debug"
targetPath="Kukolony/bin/$target/net48"
targetAssembly="Kukolony.dll"
valheimPath=""
bepinexPath=""
deployPath=""
projectPath="./Kukolony"

while [ "$#" -gt 0 ]; do
  case "$1" in
    --target)
        target="$2"; shift 2 ;;
    --target-path)
        targetPath="$2"; shift 2 ;;
    --target-assembly)
        targetAssembly="$2"; shift 2 ;;
    --valheim-path)
        valheimPath="$2"; shift 2 ;;
    --bepinex-path)
        bepinexPath="$2"; shift 2 ;;
    --deploy-path)
        deployPath="$2"; shift 2 ;;
    --project-path)
        projectPath="$2"; shift 2 ;;
    *)
        echo "Warning: Unknown argument $1" >&2; shift ;;
  esac
done

# This script does NOT build. It copies whatever is already in bin, and the build is
# what normally calls it, passing --deploy-path from MOD_DEPLOYPATH. Deploy with:
#
#     dotnet build Kukolony.sln -c Debug
#
# order of precedence: MOD_DEPLOYPATH > BEPINEX_PATH > VALHEIM_INSTALL > Environment.props
if [ -z "$deployPath" ]; then
    if [ -n "$bepinexPath" ]; then
        deployPath="$bepinexPath/plugins"
    elif [ -n "$valheimPath" ]; then
        deployPath="$valheimPath/BepInEx/plugins"
    else
        # Read the path the build itself uses rather than guessing one. This stanza used
        # to fall back to a hardcoded Linux path, mkdir -p its way into existence, and
        # report a perfectly successful copy into a directory no game has ever read - so
        # a fix was diagnosed, written, verified and "deployed" while the running game
        # kept the previous build. A deploy that cannot fail cannot be trusted.
        props="$(dirname "$0")/../Environment.props"
        install=$(sed -n 's:.*<VALHEIM_INSTALL>\(.*\)</VALHEIM_INSTALL>.*:\1:p' "$props" 2>/dev/null \
                  | sed "s:\$(HOME):$HOME:")
        [ -n "$install" ] && deployPath="$install/BepInEx/plugins"
    fi
fi

if [ -z "$deployPath" ]; then
    echo "publish.sh: no deploy path given and Environment.props has no VALHEIM_INSTALL." >&2
    exit 1
fi

# A real BepInEx install keeps core/ beside plugins/. Without this the copy below would
# happily build whatever tree it was pointed at and call it a deploy.
if [ ! -d "$deployPath/../core" ]; then
    echo "publish.sh: $deployPath is not a BepInEx install - no core/ beside plugins/." >&2
    echo "publish.sh: refusing to create it. Check VALHEIM_INSTALL in Environment.props." >&2
    exit 1
fi

# strip .dll extension
name=$(echo "$targetAssembly" | sed 's/\.dll//')

if [ "$target" = "Debug" ]; then
    plug="$deployPath/$name"
    echo "Copying $targetAssembly to $plug"

    mkdir -p "$plug"
    cp "$targetPath/$targetAssembly" "$plug"
    # copy if it exists
    [ -e "$targetPath/$name.pdb" ] && cp "$targetPath/$name.pdb" "$plug"
fi

if [ "$target" = "Release" ]; then
    packagePath="$projectPath/Package"
    mkdir -p "$packagePath/plugins"
    cp "$targetPath/$targetAssembly" "$packagePath/plugins/"
    cp "$projectPath/README.md" "$packagePath/"

    if command -v zip > /dev/null; then
        [ -e "$name.zip" ] && rm "$name.zip"
        cd "$packagePath"
        zip -r "../$name.zip" . > /dev/null
        echo "Build successful, your zip is ready for upload at $(realpath ../$name.zip)."
    else
        echo "Skipping plugin zipping, zip command isn't available."
    fi
fi
