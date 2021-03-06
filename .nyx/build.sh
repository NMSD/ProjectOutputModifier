#!/usr/bin/env bash
##########################################################################
# This is the Cake bootstrapper script for Linux and OS X.
# This file was downloaded from https://github.com/cake-build/resources
# Feel free to change this file to fit your needs.
##########################################################################

# Define directories.
SCRIPT_DIR=/Elders
TOOLS_DIR=$SCRIPT_DIR/tools
ADDINS_DIR=$TOOLS_DIR/Addins
MODULES_DIR=$TOOLS_DIR/Modules

NUGET_EXE=$TOOLS_DIR/nuget.exe
NUGET_URL=https://dist.nuget.org/win-x86-commandline/latest/nuget.exe
CAKE_VERSION=0.30.0
CAKE_EXE=$TOOLS_DIR/Cake.$CAKE_VERSION/Cake.exe

# Temporarily skip verification of addins.
export CAKE_SETTINGS_SKIPVERIFICATION='true'

# Define default arguments.
TARGET="Default"
CONFIGURATION="Release"
VERBOSITY="verbose"
DRYRUN=
SCRIPT_ARGUMENTS=()

# Parse arguments.
for i in "$@"; do
    case $1 in
        -t|--target) TARGET="$2"; shift ;;
        -c|--configuration) CONFIGURATION="$2"; shift ;;
        -v|--verbosity) VERBOSITY="$2"; shift ;;
        -d|--dryrun) DRYRUN="-dryrun" ;;
        --) shift; SCRIPT_ARGUMENTS+=("$@"); break ;;
        *) SCRIPT_ARGUMENTS+=("$1") ;;
    esac
    shift
done

# Make sure the tools folder exist.
if [ ! -d "$SCRIPT_DIR" ]; then
  mkdir "$SCRIPT_DIR"
fi

if [ ! -d "$TOOLS_DIR" ]; then
  mkdir "$TOOLS_DIR"
fi

if [ ! -d "$ADDINS_DIR" ]; then
  mkdir "$ADDINS_DIR"
fi

if [ ! -d "$MODULES_DIR" ]; then
  mkdir "$MODULES_DIR"
fi
###########################################################################
# INSTALL .NET CORE CLI
###########################################################################

export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_SYSTEM_NET_HTTP_USESOCKETSHTTPHANDLER=0

###########################################################################
# INSTALL NUGET
###########################################################################

# Download NuGet if it does not exist.
if [ ! -f "$NUGET_EXE" ]; then
    echo "Downloading NuGet..."
    curl -Lsfo "$NUGET_EXE" $NUGET_URL
    if [ $? -ne 0 ]; then
        echo "An error occurred while downloading nuget.exe."
        exit 1
    fi
fi

###########################################################################
# INSTALL CAKE
###########################################################################

if [ ! -f "$CAKE_EXE" ]; then
    mono "$NUGET_EXE" install Cake -Version $CAKE_VERSION -OutputDirectory "$TOOLS_DIR"
    if [ $? -ne 0 ]; then
        echo "An error occurred while installing Cake."
        exit 1
    fi
fi

# Make sure that Cake has been installed.
if [ ! -f "$CAKE_EXE" ]; then
    echo "Could not find Cake.exe at '$CAKE_EXE'."
    exit 1
fi

###########################################################################
# RUN BUILD SCRIPT
###########################################################################

# Start Cake
exec mono "$CAKE_EXE" .nyx/build.cake --verbosity=$VERBOSITY --configuration=$CONFIGURATION --target=$TARGET $DRYRUN "${SCRIPT_ARGUMENTS[@]}" --Paths_Tools=/Elders/tools --Paths_Addins=/Elders/tools/Addins --Paths_Modules=/Elders/tools/Modules