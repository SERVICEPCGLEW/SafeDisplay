# ⚡ SafeDisplay

SafeDisplay es una herramienta ligera y elegante para Windows diseñada específicamente para pantallas y monitores con daños físicos (líneas verticales rotas, manchas, golpes).
Crea un **borde negro seguro** que cubre las zonas dañadas de tu pantalla y delimita un nuevo "cuadro seguro" utilizable. Al hacerlo, permite que todas tus aplicaciones se maximicen respetando ese nuevo espacio sin meterse en la zona rota.

## ✨ Características Principales
- **Oculta Monitores Rotos:** Crea franjas negras estéticas (arriba, abajo, izquierda, derecha) que tapan el área defectuosa de tu monitor.
- **Integración Nativa con Windows:** Funciona como un `AppBar` de Win32, por lo que Windows maximizará tus ventanas respetando los márgenes como si tu monitor fuera físicamente más pequeño.
- **Soporte de Barra de Tareas:** Perfiles especiales de "Alineado Abajo" para que la barra de tareas de Windows 11 quede siempre usable y dentro de tu cuadro seguro.
- **Barrera Física del Mouse:** Bloquea opcionalmente que el cursor del ratón se escape hacia la zona negra o rota.
- **Filtro Nocturno Integrado:** Atenuador de brillo para trabajar de noche sin fatiga visual.

## 📥 Instalación y Uso
1. Descarga la última versión desde la sección de **[Releases](https://github.com/SERVICEPCGLEW/SafeDisplay/releases)**.
2. Descomprime y abre `SafeDisplay_v2.exe`. No requiere instalación.
3. Selecciona tu monitor y ajusta el grosor de los márgenes o usa un preajuste de tamaño.
4. Presiona **Aplicar Márgenes**.

## 🛠️ Desarrollo (Compilar)
El código fuente está escrito en C# puro (WinForms / Win32 API) y es compatible con el framework nativo de Windows.
Para compilarlo tú mismo, simplemente ejecuta el archivo `build.bat` incluido. Este script utiliza el compilador `csc.exe` predeterminado de Windows para generar el `.exe` sin necesidad de instalar Visual Studio.

---
**© Service PC Glew 2026**
👍 [Apoyar el proyecto (Matecito)](https://matecito.co/servicepcglew)
