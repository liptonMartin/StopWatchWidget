## Сборка проекта

Для создания упакованного приложения MSIX необходимо выполнить следующие команды в терминале (bash), запущенном от имени
администратора:

1. пробросить путь до winapp

```bash
WINAPP="/c/Users/rvova/.nuget/packages/microsoft.windows.sdk.buildtools.winapp/0.7.1/tools/win-x64/winapp.exe"
```

2.

```bash
"$WINAPP" package StopwatchWidget.csproj -c Release --arch x64 --generate-cert --install-cert
```
