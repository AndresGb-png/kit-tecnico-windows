# Kit técnico para Windows

Programas para técnicos en sistemas, creados por **Julio Gutiérrez**: diagnosticar, respaldar, reinstalar, reparar y documentar el servicio de un equipo.

Son portables: cada uno es un solo archivo, no se instala y cabe en una USB. No necesitan nada adicional en Windows 10 y 11.

## Programas

| Programa | Para qué sirve |
|---|---|
| **ChequeadorPC** | Revisa hardware y sistema, explica cada resultado en lenguaje sencillo y trae pruebas guiadas de teclado, ratón, sonido, cámara, pantalla y puertos |
| **RespaldoPC** | Guarda los datos del cliente antes de formatear, comprueba cada copia y los devuelve a un Windows limpio sin pisar archivos distintos |
| **PosformateoPC** | Instala programas por perfiles con winget o desde una carpeta, y aplica ajustes básicos |
| **ReparadorPC** | Diagnostica Windows y ejecuta las reparaciones habituales, con informe de antes y después |
| **FichaServicio** | Registra cliente, equipo, trabajo, costos y garantía, y genera comprobantes en PDF |
| **LimpiezaPC** | Revisa lo que arranca con Windows, señala lo sospechoso y lo desactiva sin borrar nada, con opción de deshacer. Analiza con Windows Defender |
| **Optimizador de Windows** | Script que limpia archivos y reduce el uso de RAM, CPU y disco |

## Descargar

Los ejecutables están en la sección [Releases](../../releases) de este repositorio.

Como no están firmados, Windows puede mostrar «Windows protegió su PC». Si lo descargaste de aquí y quieres ejecutarlo, pulsa **Más información** y luego **Ejecutar de todas formas**.

Ponlos todos en la misma carpeta: la Ficha de servicio usa `ChequeadorPC.exe` para el diagnóstico de antes y después.

## Qué tan probado está

Las pruebas se hicieron en varios equipos con Windows 10 y 11, sin embargo, otros modelos y versiones pueden comportarse distinto, así que prueba en un equipo que no sea de un cliente antes de depender de ellos.

**Aún sin probar de verdad:**

- Discos SATA y HDD en el Chequeador.
- En RespaldoPC: un disco dañado, archivos de OneDrive solo en la nube, un disco de otro Windows y la instalación de controladores al restaurar.
- En PosformateoPC: el botón "Activar winget" y el cambio real de nombre del equipo.
- En ReparadorPC: "Restablecer la red" y "Programar la reparación del disco".
- En ChequeadorPC: la prueba de cámara y la de pantalla.
- En FichaServicio: exportar a Excel, hacer copia de seguridad, guardar «Datos de mi negocio» y elegir el logo desde la ventana.
- En LimpiezaPC: virus reales, el análisis completo de Defender y un análisis que encuentre amenazas. Tampoco la detección de cambios en Winlogon, AppInit_DLLs, eventos WMI, extensiones impuestas, accesos directos de navegadores ni el archivo hosts.
- Optimizador de Windows: una revisión de pruebas propia, como la de los demás programas.

## Limitaciones

- Desde Windows no se puede saber si una ranura de RAM vacía está dañada, ni si un puerto sin nada conectado funciona. El Chequeador lo dice y explica cómo comprobarlo.
- La temperatura depende de los sensores que el fabricante expone a Windows. En algunos equipos no está disponible.
- RespaldoPC no copia las contraseñas del navegador ni los programas instalados.
- LimpiezaPC **no es un antivirus** y no elimina virus. Busca señales en lo que arranca con Windows: «sospechoso» no prueba que haya un virus, y no encontrar nada no prueba que el equipo esté limpio.
- Algunas funciones requieren permisos de administrador. Los programas los piden al abrirse y, si se rechazan, siguen con lo que puedan.

## Privacidad

Ningún programa envía datos a un servidor propio. Sí usan internet en estos casos:

- ChequeadorPC y ReparadorPC prueban la conexión con ping a `1.1.1.1` y `8.8.8.8`; ReparadorPC también consulta `www.microsoft.com` y `time.windows.com`.
- PosformateoPC y RespaldoPC usan winget, que consulta su catálogo.
- DISM, en ReparadorPC, puede descargar archivos de Windows Update.
- LimpiezaPC abre VirusTotal con la huella de un archivo solo si pulsas ese botón. El archivo no se sube.

Dos cosas requieren cuidado:

- **Respaldos:** si incluyes las redes Wi-Fi, sus claves quedan legibles. Guarda el respaldo en un lugar seguro y bórralas con el botón que aparece al restaurar.
- **Fichas de servicio:** los datos de clientes se guardan sin cifrar junto al programa. No anotes contraseñas en ellas y protege esa carpeta, sobre todo en una USB.

## Optimizador de Windows

Es un script que **cambia la configuración del equipo**: desactiva servicios y tareas de telemetría, efectos visuales, aplicaciones en segundo plano y Game DVR; ajusta el plan de energía; limpia temporales y cachés; y restablece Winsock, lo que puede afectar a una VPN. En equipos de escritorio también desactiva la hibernación. Crea un punto de restauración antes de empezar y guarda un registro en `C:\OptimizadorWindows`. Lee [el script](OptimizadorWindows/Optimizador_Windows.bat) antes de usarlo.

## Compilar desde el código

No hace falta instalar nada: se usa el compilador de .NET Framework que ya trae Windows.

```powershell
.\compilar.ps1
```

Cada ejecutable queda en la carpeta de su programa. El código compartido, como la sección de créditos, está en `comun`.

## Autor

Creado por **Julio Gutiérrez** ([AndresGb-png](https://github.com/AndresGb-png)).

## Licencia

[MIT](LICENSE). Puedes usar y modificar este código manteniendo el aviso de autor. Se ofrece sin garantía: revisa lo que hace antes de usarlo en equipos de clientes.
