# Britannia Reborn Launcher

Launcher oficial para el shard. WPF .NET 10, single-file self-contained .exe.

## Estructura del proyecto
```
britannia-reborn-launcher/
├── BritanniaRebornLauncher.csproj
├── App.xaml / App.xaml.cs
├── MainWindow.xaml / .cs       ← login
├── WizardWindow.xaml / .cs     ← UO no detectado
├── Config.cs                   ← IP/puerto compilados
├── LauncherCore.cs             ← lógica launch
├── LauncherSettings.cs         ← persistencia settings
└── Resources/
    ├── logo.png
    ├── banner.png
    └── login-bg.png
```

## Antes de compilar

1. Coloca las 3 imágenes en `Resources/`:
   - `logo.png` (logo redondo del shard, no usado en UI inicial — reservado para futuras pantallas)
   - `banner.png` (banner ancho, usado en wizard)
   - `login-bg.png` (fondo del login, ocupa toda la ventana)
2. (Opcional) `Resources/icon.ico` para el icono del .exe en explorer

## Compilar

```bash
dotnet publish -c Release -r win-x64
```

El .exe single-file aparece en `bin/Release/net10.0-windows/win-x64/publish/BritanniaReborn.exe` (~80 MB).

## Distribución

El launcher por sí solo no juega — necesita **ClassicUO** al lado. Empaqueta:

```
Britannia-Reborn-Launcher/
├── BritanniaReborn.exe       ← launcher
└── cuo/                       ← ClassicUO completo
    ├── cuo.exe
    ├── cuo.dll
    ├── ClassicUO.*.dll
    ├── FNA.dll
    ├── SDL3.dll
    └── (resto de archivos del PLAY/ del shard actual)
```

Sube esto a tu Discord o web como `.zip`. Los players descargan, descomprimen, ejecutan `BritanniaReborn.exe`.

## Flujo del launcher

1. **Splash invisible** → `App.OnStartup` detecta UO oficial:
   - Busca en rutas estándar (`C:\Program Files (x86)\Electronic Arts\Ultima Online Classic` y otras 5 candidatas)
   - Si NO encuentra → abre `WizardWindow`
2. **WizardWindow**:
   - Botón "Descargar UO oficial" → abre `https://www.uo.com/client-download/` en navegador
   - Botón "Ya lo tengo" → file picker, valida que la carpeta tiene `anim.mul` y `map0.mul`/`map0LegacyMUL.uop`
   - Botón "Salir" → cierra
3. **MainWindow** (login):
   - Carga settings persistidos (`%APPDATA%/BritanniaReborn/settings.json`)
   - Si `AutoLogin=true` y credenciales guardadas → lanza al cargar
   - Botón Play → guarda settings, lanza ClassicUO con args:
     ```
     -ip 134.255.219.238 -port 2593 -shardtype 2 -uopath "C:\..."
     -username X -password X [-fastlogin]
     ```
   - Cierra launcher tras lanzar.

## Configuración del shard

Edita `Config.cs`:
```csharp
public const string ServerHost = "134.255.219.238";  // IP del shard
public const int ServerPort = 2593;
public const int ShardType = 2;
public const string ClientVersion = "7.0.114.40";    // versión UO recomendada
```

La IP queda **compilada en el binario** — invisible al abrir el .exe con notepad. Solo visible si decompilan con dnSpy/ILSpy (estándar shard).

## Settings persistidos

`%APPDATA%\BritanniaReborn\settings.json`:
```json
{
  "UoPath": "C:\\Program Files (x86)\\...",
  "LastUsername": "...",
  "SavedPassword": "...",  // solo si SavePassword=true
  "AutoLogin": false,
  "SavePassword": false
}
```

## Pendiente / mejoras futuras

- Auto-update del launcher (verificar versión via web request, descargar nuevo .exe)
- Patcher: descargar/actualizar ClassicUO embebido al arrancar
- Verificación de integridad de archivos UO (hash de `anim.mul` vs versión esperada)
- Splash/loading screen visual mientras verifica
- Soporte multi-shard con dropdown
