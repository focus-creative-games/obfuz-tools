# obfuz-tools
tools for obfuz.


# Build to one exe

dotnet publish "DeobfuscateStackTrace/DeobfuscateStackTrace.csproj" -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false