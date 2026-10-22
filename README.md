# EchoCore Bootstrapper

Installs and updates the EchoCore client and Studio. Older years (2018, 2020) are downloaded the first time a game or place of that year is opened.

- Installs to `%LocalAppData%\EchoCore`, no administrator rights needed
- Downloads files only from `www.echocr.xyz` and checks their SHA-256
- Handles the Play and Edit buttons on the website
- Can be removed from Windows Settings → Apps

## Build

Requires the .NET SDK.

```
dotnet build -c Release
```
