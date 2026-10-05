# BlockAlignTool

Plugin de AutoCAD en C# con dos comandos para trabajar con bloques sobre polilíneas: alinear bloques existentes a una polilínea e insertar un bloque a intervalos regulares a lo largo de ella.

Sirve para elementos que se repiten sobre un eje: postes, luminarias, señales, árboles, apoyos, marcas de abscisado.

## Comandos

### `ALINEARBLOQUES`

Toma bloques ya insertados en el dibujo, los mueve al punto más cercano de la polilínea elegida y, si se quiere, los rota según la tangente de la polilínea en ese punto.

1. Selecciona la polilínea de referencia.
2. Selecciona los bloques a alinear (ventana o uno por uno).
3. Indica si se rotan según la tangente (`Sí` por defecto).

### `INSERTARBLOQUES`

Inserta un bloque por nombre cada cierta distancia a lo largo de una polilínea, rotado según la tangente.

1. Selecciona la polilínea.
2. Escribe el nombre del bloque. Debe existir en el dibujo.
3. Indica la distancia entre bloques (por defecto `5.0`).
4. Indica la escala (por defecto `1.0`).
5. Indica el desplazamiento lateral: positivo a la izquierda del sentido de la polilínea, negativo a la derecha, `0` sobre ella.
6. Indica si se inserta también en el punto de inicio (`No` por defecto).

Antes de insertar, el comando informa la longitud de la polilínea y cuántos bloques va a crear.

## Qué tiene en cuenta

- Funciona con `Polyline` (LWPOLYLINE), `Polyline2d` y `Polyline3d`. Las tres se tratan con la API de `Curve`, así que los tramos en arco se resuelven con la tangente real.
- Los bloques con atributos se insertan con sus atributos.
- Los bloques insertados heredan la capa de la polilínea.
- La rotación se calcula sobre el plano XY, también en polilíneas 3D.

## Requisitos

| | |
|---|---|
| AutoCAD | 2024. También Civil 3D 2024 y demás verticales basados en AutoCAD 2024 |
| Framework | .NET Framework 4.8 |
| Sistema | Windows 64 bits |

AutoCAD 2025 y 2026 usan .NET 8: para esas versiones hay que cambiar `TargetFramework` a `net8.0-windows` y apuntar a sus DLL. Esa variante no está probada. AutoCAD LT no carga plugins .NET.

## Compilar

Con Visual Studio 2022, abre `BlockAlignTool.csproj` y compila en `Release`. Por línea de comandos:

```
dotnet build BlockAlignTool.csproj -c Release
```

La DLL queda en `bin\Release\net48\BlockAlignTool.dll`.

El proyecto busca las DLL de AutoCAD (`accoremgd`, `acdbmgd`, `acmgd`) en `C:\Program Files\Autodesk\AutoCAD 2024`. Si tu instalación o tu ObjectARX SDK están en otra carpeta, crea un archivo `BlockAlignTool.local.props` junto al `.csproj` (no se versiona):

```xml
<Project>
  <PropertyGroup>
    <AcadDir>D:\Ruta\AutoCAD 2024</AcadDir>
  </PropertyGroup>
</Project>
```

## Instalar

**Sesión actual.** En AutoCAD ejecuta `NETLOAD` y elige `BlockAlignTool.dll`.

**Permanente.** Ejecuta `APPLOAD`, abre **Contenido** en el grupo *Startup Suite* y agrega `BlockAlignTool.dll`.

**Por script.** En `acaddoc.lsp`:

```lisp
(command "_.NETLOAD" "C:\\Ruta\\BlockAlignTool.dll")
```

## Personalizar

Cambiar la capa de inserción. En la línea `br.Layer = lineEnt.Layer;`:

```csharp
br.Layer = "MI_CAPA";
```

Agregar un giro adicional. Después de `br.Rotation = angle;`:

```csharp
br.Rotation += Math.PI / 2; // +90°
```

## Limitaciones

- `INSERTARBLOQUES` pide el nombre del bloque por teclado; no hay selector.
- Los bloques se insertan en el espacio modelo.
- No se considera un UCS distinto del universal.

## Autor

Andrés G. Rodríguez · [AGRDB – AGR Digital Building](https://agrdb.com)

## Licencia

[MIT](LICENSE)
