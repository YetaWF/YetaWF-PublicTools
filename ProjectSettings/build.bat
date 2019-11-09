rmdir /S /Q bin
dotnet publish -r win-x64 -c Release /p:PublishSingleFile=true -o bin/Windows
dotnet publish -r linux-x64 -c Release /p:PublishSingleFile=true -o bin/Linux
