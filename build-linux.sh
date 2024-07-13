#!/bin/sh

ROOTCMD=sudo
NETVER=6.0

cd SIR2CPU
dotnet build
cd ..
$ROOTCMD cp bin/net$NETVER/* /usr/bin