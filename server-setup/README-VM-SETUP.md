# Setup del patcher en la VM Linux

Pasos one-time para que el launcher pueda descargar los `.mul` actualizados.

## 1. Instalar nginx

```bash
sudo apt update
sudo apt install nginx -y
```

## 2. Crear estructura de directorios

```bash
sudo mkdir -p /srv/uo-mul/files
sudo chown -R $(whoami):$(whoami) /srv/uo-mul
```

## 3. Instalar el script de publicación

```bash
sudo cp uo-publish-map.sh /usr/local/bin/uo-publish-map
sudo chmod +x /usr/local/bin/uo-publish-map
```

**Editar las variables** dentro del script:
- `SOURCE_DIR`: carpeta donde están los `.mul` que CentrED actualiza
- `PATCH_DIR`: por defecto `/srv/uo-mul`

## 4. Configurar nginx

```bash
sudo cp nginx-uo-mul.conf /etc/nginx/sites-available/uo-mul
sudo ln -s /etc/nginx/sites-available/uo-mul /etc/nginx/sites-enabled/
sudo nginx -t            # verifica config
sudo systemctl reload nginx
sudo ufw allow 8080/tcp  # si tienes ufw activo
```

## 5. Probar (manualmente la primera vez)

```bash
uo-publish-map
```

Debería listar los archivos copiados y el manifest. Verifica:

```bash
curl http://localhost:8080/manifest.json
curl -I http://localhost:8080/files/map0.mul
```

Desde tu PC fuera de la VM:

```
http://134.255.219.238:8080/manifest.json
```

Debería devolver el JSON con la lista de archivos + hashes.

## 6. Permisos para el comando in-game

ModernUO ejecuta como su usuario propio (probablemente `modernuo`). Necesita
poder ejecutar `/usr/local/bin/uo-publish-map` Y poder escribir en
`/srv/uo-mul/`. Dos formas:

**Opción simple — ModernUO escribe directo en /srv/uo-mul/**:
```bash
sudo chown -R modernuo:modernuo /srv/uo-mul
```

**Opción más segura — sudo sin password para ese script concreto**:
```bash
sudo visudo
# Añadir línea:
modernuo ALL=(ALL) NOPASSWD: /usr/local/bin/uo-publish-map
```
Y en `PublicarMapaCommand.cs` cambiar `FileName = "/usr/local/bin/uo-publish-map"`
por `FileName = "sudo"` con `ArgumentList.Add("/usr/local/bin/uo-publish-map")`.

Sugerencia: opción 1 (más simple, no es código sensible).

## Workflow operativo

1. Editas el mapa en CentrED (cambios se vuelcan a `$SOURCE_DIR`).
2. Estando in-game como Admin: `[PublicarMapa`
3. ModernUO ejecuta el script → copia + manifest publicado.
4. Players cierran y abren el launcher → ven actualización → descargan los `.mul` nuevos → entran al juego.

Total: ~2 segundos entre tu cambio en CentrED y la disponibilidad para todos los players.

## Diagnostico

- Logs nginx: `/var/log/nginx/uo-mul-access.log` y `uo-mul-error.log`
- Logs ModernUO: `/var/log/modernuo/modernuo.log` (busca `[PublicarMapa]`)
- Test manual: `sudo -u modernuo /usr/local/bin/uo-publish-map`
