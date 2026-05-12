#!/bin/bash
# /usr/local/bin/uo-publish-map
#
# Republica los .mul del mapa Felucca a la carpeta servida por nginx para
# que el launcher de los players los descargue.
#
# Setup en la VM (una vez):
#   sudo cp uo-publish-map.sh /usr/local/bin/uo-publish-map
#   sudo chmod +x /usr/local/bin/uo-publish-map
#   sudo mkdir -p /srv/uo-mul/files
#   sudo chown -R $(whoami):$(whoami) /srv/uo-mul
#
# Editar las variables de abajo según tu shard.

set -e

# ============ EDITAR SEGÚN INSTALACIÓN ============
# Carpeta donde CentrED+ vuelca los cambios (fuente de verdad para el patcher).
SOURCE_DIR="${UO_SOURCE_DIR:-/home/modernuo/uo-mapping}"

# Carpeta que el servidor ModernUO carga al arrancar. Mantenemos sincronía
# desde SOURCE_DIR para que tras un restart server vea lo mismo que el cliente.
SERVER_DIR="${UO_SERVER_DIR:-/home/modernuo/uo-classic}"

# Carpeta servida por nginx
PATCH_DIR="${UO_PATCH_DIR:-/srv/uo-mul}"

# Archivos a publicar (los que el shard modifica)
# NOTA: el mapa Felucca va en map0LegacyMUL.uop (formato cliente moderno),
# no map0.mul. statics y staidx siguen siendo .mul. Gumps también .mul
# (legacy) porque UOFiddler los edita en ese formato y el cliente los
# carga prioritariamente si gumpartLegacyMUL.uop está renombrado a .bak.
FILES=(
    # Mapa + statics
    "map0LegacyMUL.uop"
    "staidx0.mul"
    "statics0.mul"
    # Gumps custom (chat HUD, organizadores, etc.)
    "gumpart.mul"
    "gumpidx.mul"
    "gump.def"
    # Art (items, iconos de inventario)
    "art.mul"
    "artidx.mul"
    "tiledata.mul"
    # Animaciones (monturas custom: Kirín, Jabalí, Wyrm, etc.)
    "anim.mul"
    "anim.idx"
    "anim2.mul"
    "anim2.idx"
    "anim3.mul"
    "anim3.idx"
    "anim4.mul"
    "anim4.idx"
    "anim5.mul"
    "anim5.idx"
    "animdata.mul"
    "bodyconv.def"
    "body.def"
    "mobtypes.txt"
    # Multi-tiles (casas, barcos, addons)
    "multi.mul"
    "multi.idx"
)
# ===================================================

mkdir -p "$PATCH_DIR/files"

VERSION=$(date -u +%Y%m%d-%H%M%S)
echo "Publicando assets cliente (mapa + gumps) version $VERSION..."

# Copiar archivos a (1) carpeta del patcher cliente y (2) carpeta del server
for f in "${FILES[@]}"; do
    src="$SOURCE_DIR/$f"
    dst_patch="$PATCH_DIR/files/$f"
    dst_server="$SERVER_DIR/$f"
    if [ ! -f "$src" ]; then
        echo "WARN: $src no existe, skip"
        continue
    fi
    cp -f "$src" "$dst_patch"
    echo "  publicado en patcher: $f ($(stat -c%s "$dst_patch") bytes)"
    if [ -d "$SERVER_DIR" ]; then
        cp -f "$src" "$dst_server"
        echo "  sincronizado en server-source: $f"
    fi
done

# Generar manifest.json con SHA256 de cada archivo
TMP_MANIFEST="$PATCH_DIR/manifest.json.tmp"
{
    echo "{"
    echo "  \"version\": \"$VERSION\","
    echo "  \"updatedAt\": \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\","
    echo "  \"files\": ["

    first=1
    for f in "${FILES[@]}"; do
        path="$PATCH_DIR/files/$f"
        if [ ! -f "$path" ]; then continue; fi
        size=$(stat -c%s "$path")
        sha=$(sha256sum "$path" | awk '{print $1}')
        if [ $first -eq 0 ]; then echo ","; fi
        first=0
        printf "    { \"name\": \"%s\", \"size\": %d, \"sha256\": \"%s\" }" "$f" "$size" "$sha"
    done
    echo ""
    echo "  ]"
    echo "}"
} > "$TMP_MANIFEST"

mv -f "$TMP_MANIFEST" "$PATCH_DIR/manifest.json"
echo "Manifest publicado en $PATCH_DIR/manifest.json"
echo "Assets cliente publicados OK (version $VERSION)."
echo ""
echo "RECORDATORIO: para que el SERVER aplique los cambios (no solo el cliente),"
echo "  reinicia ModernUO cuando convenga: 'systemctl restart modernuo' o desde el panel."
