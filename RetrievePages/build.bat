rmdir /S /Q bin
dotnet publish --self-contained -r win-x64 -c Release /p:PublishSingleFile=true -o bin/Windows
dotnet publish --self-contained -r linux-x64 -c Release /p:PublishSingleFile=true -o bin/Linux
