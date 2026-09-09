using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace BioshockVrLauncher
{
    internal static class UiLanguage
    {
        private static bool _english;
        private static bool? _forcedForTest;
        private static readonly Dictionary<string, string> Exact = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "Imagen", "Image" }, { "Cámara", "Camera" }, { "Manos", "Hands" },
            { "Giro", "Turning" }, { "Gestos", "Gestures" }, { "Cine", "Cinematics" },
            { "Preparando configuración…", "Preparing configuration…" },
            { "Guardar", "Save" }, { "Guardar e iniciar", "Save and launch" }, { "Recargar", "Reload" },
            { "Créditos", "Credits" }, { "Imagen del visor", "Headset image" },
            { "Resolución", "Resolution" }, { "Modo de renderizado", "Rendering mode" },
            { "Calidad DLSS", "DLSS quality" }, { "Resolución interna", "Internal resolution" },
            { "Opciones gráficas del juego", "Game graphics options" },
            { "Shaders de alto detalle", "High-detail shaders" }, { "Sombras", "Shadows" },
            { "Reflejos", "Reflections" }, { "Posprocesado", "Post-processing" },
            { "Ondulaciones del agua", "Water ripples" },
            { "Partículas de alta calidad", "High-quality particles" },
            { "Particulas de alta calidad", "High-quality particles" },
            { "Distorsión", "Distortion" }, { "Distorsion", "Distortion" },
            { "Posprocesado de alta calidad", "High-quality post-processing" },
            { "Detalle de fluidos", "Fluid detail" }, { "Bajo", "Low" }, { "Alto", "High" },
            { "Desactivado", "Off" }, { "Activado", "On" },
            { "Valores predeterminados", "Restore defaults" },
            { "* Alto impacto en el Rendimiento", "* High performance impact" },
            { "Reflejos y ondulaciones desactivados; resto activado.", "Reflections and ripples off; everything else on." },
            { "Pendiente", "Pending" }, { "Abrir dlss.ini", "Open dlss.ini" },
            { "Desbloquear", "Unlock" }, { "Restaurar fila", "Restore row" },
            { "Sí", "Yes" }, { "No", "No" },
            { "Cámara y escala", "Camera and scale" }, { "Manos y apuntado", "Hands and aiming" },
            { "Movimiento y giro", "Movement and turning" }, { "Ataque por gesto", "Gesture attack" },
            { "Cinemáticas y efectos", "Cinematics and effects" }, { "HUD y ayudas", "HUD and assists" },
            { "Archivos y ayuda ▾", "Files and help ▾" }, { "Abrir vrpreset.ini", "Open vrpreset.ini" },
            { "Mod original de Mohamad Balouza · Fork DLSS/DLAA de Beren5556", "Original mod by Mohamad Balouza · DLSS/DLAA fork by Beren5556" },
            { "Abrir Bioshock.ini", "Open Bioshock.ini" }, { "Ver copias de seguridad", "View backups" },
            { "Créditos y licencias", "Credits and licenses" },
            { "Resolución del visor", "Headset resolution" },
            { "F1 abre el menú en el visor y navega por las opciones disponibles.\nF2: anterior / − · F3: siguiente / + · En opciones gráficas, F4 cambia el valor.",
              "F1 opens the in-headset menu and moves through the available options.\nF2: previous / − · F3: next / + · In Graphics Options, F4 changes the value." },
            { "Píxeles por lado y por ojo. Se aplica el mismo valor a la anchura y la altura.",
              "Pixels per side and per eye. The same value is applied to width and height." },
            { "Cada tramo cambia 100 píxeles de resolución interna por lado. El porcentaje se calcula respecto a la salida del visor.",
              "Each step changes the internal resolution by 100 pixels per side. The percentage is calculated against the headset output." },
            { "Abre el juego una primera vez sin el mod y ciérralo; después pulsa Recargar.",
              "Run the game once without the mod, close it, then select Reload." },
            { "Resolución cuadrada por ojo. Guarda los cambios con el juego cerrado.",
              "Square resolution per eye. Save changes while the game is closed." },
            { "Ajuste no disponible en la configuración actual; no se modifica automáticamente.",
              "This setting is unavailable in the current configuration and will not be changed automatically." },
            { "Configuración de ejemplo · no modifica archivos del juego", "Sample configuration · does not modify game files" },
            { "Vista previa · 0.2.12 · sin modificar archivos del juego", "Preview · 0.2.12 · game files unchanged" },
            { "UU/metro", "UU/metre" }, { "grados", "degrees" }, { "multiplicador", "multiplier" },
            { "metros", "metres" }, { "por segundo", "per second" }, { "vértices", "vertices" },
            { "0 · La VR sigue controlando la cámara", "0 · VR keeps controlling the camera" },
            { "1 · Cámara y manos dirigidas por el juego", "1 · Game-controlled camera and hands" },
            { "2 · Cámara del juego + movimiento de cabeza", "2 · Game camera + head movement" }
        };

        private static readonly Dictionary<string, string[]> Parameters = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "worldScale", new[] { "World scale", "Game units per real metre. Higher values make the world feel smaller; lower values make it feel larger and also change physical locomotion distance." } },
            { "headUpUu", new[] { "Additional head height", "Moves the viewpoint vertically without changing world scale. Positive values move it up; negative values move it down." } },
            { "headFwdUu", new[] { "Head forward offset", "Moves the viewpoint forward or backward relative to the body. Positive values move it forward." } },
            { "ipdMm", new[] { "Virtual interpupillary distance (IPD)", "Distance between the two eye cameras. It should normally match the headset IPD; an incorrect value changes perceived scale and may cause discomfort." } },
            { "gameFovDeg", new[] { "Game target FOV (advanced)", "Horizontal angle the mod may write to the game. The installed custom DLL normally prioritizes the headset's full FOV." } },
            { "handScaleL", new[] { "Left hand size", "Visual scale of the plasmid hand. 1.00 keeps the base size; 1.10 makes it 10% larger." } },
            { "handScaleR", new[] { "Right hand size", "Visual scale of the weapon hand. 1.00 keeps the base size; 1.10 makes it 10% larger." } },
            { "wScale", new[] { "Weapon size", "Uniform weapon-model scale around the grip. This does not change hand or world size." } },
            { "aimTrimLPitch", new[] { "Left aiming pitch", "Corrects the up/down firing direction of the left plasmid hand without moving the visible hand model." } },
            { "aimTrimLYaw", new[] { "Left aiming yaw", "Corrects the left/right firing direction of the left plasmid hand." } },
            { "aimTrimRPitch", new[] { "Right aiming pitch", "Corrects the up/down firing direction of the right weapon hand without moving the visible weapon model." } },
            { "aimTrimRYaw", new[] { "Right aiming yaw", "Corrects the left/right firing direction of the right weapon hand." } },
            { "aimPosLFwd", new[] { "Left origin: forward/back", "Moves the plasmid shot origin along the hand direction without moving the visible hand." } },
            { "aimPosLRight", new[] { "Left origin: lateral", "Moves the left shot origin sideways. Positive values move it towards the hand's local right." } },
            { "aimPosLUp", new[] { "Left origin: height", "Moves the left shot origin up or down relative to the hand. Positive values move it up." } },
            { "aimPosRFwd", new[] { "Right origin: forward/back", "Moves the weapon shot origin along the hand direction without moving the visible weapon." } },
            { "aimPosRRight", new[] { "Right origin: lateral", "Moves the weapon shot origin sideways. Positive values move it towards the hand's local right." } },
            { "aimPosRUp", new[] { "Right origin: height", "Moves the right shot origin up or down relative to the hand. Positive values move it up." } },
            { "laserOn", new[] { "Aiming laser", "Shows a dotted line along the real aiming ray. Useful for calibration and optional during play." } },
            { "aimDotOn", new[] { "Mod aiming dot", "Shows a dot on the true shot trajectory, independently of the game's flat crosshair." } },
            { "aimDotDistM", new[] { "Aiming-dot distance", "Distance at which the mod aiming dot appears. It is visible only when the dot is enabled." } },
            { "aimDotSizeDeg", new[] { "Aiming-dot size", "Angular diameter of the dot, keeping a similar apparent size at different distances." } },
            { "autoVr", new[] { "Enable VR mode automatically", "Applies the complete VR configuration when the game starts. Keep this enabled when using the launcher." } },
            { "bodyRate", new[] { "Body turning smoothness", "Rate at which the body reaches the head orientation. Zero is immediate; higher values make it progressive." } },
            { "bodyDeadzoneDeg", new[] { "Body dead zone", "Head yaw difference allowed before the body starts following it. Higher values reduce small corrections but may feel more abrupt." } },
            { "moveDirInstant", new[] { "Immediate movement direction", "Makes walking direction follow the head immediately during fast turns." } },
            { "turnScale", new[] { "Smooth-turn speed", "Multiplier applied to stick turning. 1.00 is the base rate; lower is slower and higher is faster." } },
            { "snapTurn", new[] { "Snap turning", "Replaces smooth turning with fixed-angle steps. Disabled keeps continuous turning." } },
            { "snapAngleDeg", new[] { "Angle per step", "Degrees in each discrete turn. This applies only when snap turning is enabled." } },
            { "swingOn", new[] { "Attack by swinging the wrench", "Turns a fast right-hand movement into an attack press while holding the wrench." } },
            { "swingThreshold", new[] { "Required gesture speed", "Minimum hand speed required to attack. Higher values require a stronger gesture and reduce accidental activation." } },
            { "swingRearm", new[] { "Gesture rearm speed", "The hand must slow below this value before another hit is accepted. It must be lower than the attack threshold." } },
            { "swingCooldownMs", new[] { "Delay between hits", "Minimum time between two gesture attacks. Increasing it prevents double hits." } },
            { "swingPulseMs", new[] { "Attack-press duration", "How long the mod holds the attack trigger. If too short, the game may not detect the hit." } },
            { "swingDelayMs", new[] { "Delay before attack", "Time between detecting the gesture and pressing attack. Zero responds immediately." } },
            { "swingHeadRel", new[] { "Measure gesture relative to head", "Subtracts general head movement from hand speed, reducing accidental attacks while moving the whole body." } },
            { "cineBarsHidden", new[] { "Hide cinematic black bars", "Removes the letterbox bars while keeping the image underneath complete and unstretched." } },
            { "cineDrive", new[] { "Behaviour during cinematics", "Chooses whether VR remains in control, the directed game camera is fully respected, or head movement is added to that camera." } },
            { "cineSubsInFrame", new[] { "Subtitles inside the 3D image", "Embeds subtitles in each eye image when enabled, where they may appear doubled. Disabled keeps them on the usually clearer HUD panel." } },
            { "effectsInFrame", new[] { "Screen effects across the whole view", "Places water, damage and flashes across the full view instead of the HUD panel." } },
            { "effectMaxVerts", new[] { "Effect vertex limit (advanced)", "Maximum vertices in an untextured draw treated as a screen effect. Eight is the tested value." } },
            { "postFxRtOnly", new[] { "Filter effects by render target", "Keeps effects such as alcohol blur in the view while avoiding ordinary HUD textures. Enabled is recommended." } },
            { "lockOnDisabled", new[] { "Disable magnetic aim assist", "Prevents controller assist from pulling aim towards enemies. Enabled is usually more natural with motion controllers." } },
            { "crosshairVisible", new[] { "Show the game's flat crosshair", "Restores the original 2D crosshair independently of the mod's three-dimensional aiming dot." } },
            { "hudQuadDistM", new[] { "HUD panel distance", "Distance from the eyes to the floating HUD panel. Moving it farther away reduces its apparent size unless width is also increased." } },
            { "hudQuadWidthM", new[] { "HUD panel width", "Physical width of the floating panel. Increasing it makes the HUD larger inside the headset." } },
            { "hudQuadUpM", new[] { "HUD panel height", "Vertical panel offset. Positive values move it up and negative values move it down." } }
        };

        private static readonly KeyValuePair<string, string>[] Fragments = {
            Pair("Configuración", "Configuration"), Pair("configuración", "configuration"),
            Pair("Resolución", "Resolution"), Pair("resolución", "resolution"),
            Pair("Guardar cambios", "Save changes"), Pair("Guardar e iniciar", "Save and launch"),
            Pair("Guardar", "Save"), Pair("Recargar", "Reload"), Pair("Abrir", "Open"),
            Pair("Juego en ejecución", "Game running"), Pair("juego está abierto", "game is running"),
            Pair("JUEGO ENCONTRADO", "GAME FOUND"), Pair("JUEGO NO ENCONTRADO", "GAME NOT FOUND"),
            Pair("No se ha podido", "Could not"), Pair("No se pudo", "Could not"), Pair("No se puede", "Cannot"),
            Pair("No se encuentra", "Could not find"), Pair("no se encuentra", "could not find"),
            Pair("Error al", "Error while"), Pair("Cambios pendientes", "Pending changes"),
            Pair("cambios sin guardar", "unsaved changes"), Pair("cambios pendientes", "pending changes"),
            Pair("¿Quieres", "Do you want to"), Pair("¿", ""),
            Pair("Revisar", "Review"), Pair("REVISAR", "REVIEW"), Pair("PENDIENTE", "PENDING"),
            Pair("Estado", "Status"), Pair("estado", "status"), Pair("Aplicado", "Applied"),
            Pair("guardado", "saved"), Pair("guardando", "saving"), Pair("cargar", "load"),
            Pair("cerrado", "closed"), Pair("cerrar", "close"), Pair("reinicia el juego", "restart the game"),
            Pair("el lanzador", "the launcher"), Pair("El lanzador", "The launcher"),
            Pair("el juego", "the game"), Pair("del juego", "of the game"),
            Pair("en el visor", "in the headset"), Pair("por ojo", "per eye"),
            Pair("anterior", "previous"), Pair("siguiente", "next"), Pair("cambiar", "change"),
            Pair("Selecciona", "Select"), Pair("seleccionada", "selected"),
            Pair("Advertencia", "Warning"), Pair("Créditos y licencias", "Credits and licenses"),
            Pair("versión", "version"), Pair("Versión", "Version"), Pair("carpeta", "folder"),
            Pair("fichero", "file"), Pair("archivos", "files"), Pair("activado", "enabled"),
            Pair("desactivado", "disabled"), Pair("Alto impacto en el Rendimiento", "High performance impact")
        };

        private static KeyValuePair<string, string> Pair(string source, string target)
        {
            return new KeyValuePair<string, string>(source, target);
        }

        internal static void Initialize(string dlssPath)
        {
            _english = _forcedForTest.HasValue ? _forcedForTest.Value :
                string.Equals(ReadIniValue(dlssPath, "ui", "language"), "en", StringComparison.OrdinalIgnoreCase);
        }
        internal static bool IsEnglish { get { return _english; } }
        internal static string Text(string spanish, string english) { return _english ? english : spanish; }

        internal static void SetForTest(bool english) { _forcedForTest = english; _english = english; }
        internal static void ClearTestLanguage() { _forcedForTest = null; }

        internal static void Localize(ParamDef definition)
        {
            if (!_english || definition == null) return;
            string[] translated;
            if (Parameters.TryGetValue(definition.Key, out translated))
            {
                definition.Label = translated[0];
                definition.Description = translated[1];
            }
            definition.Unit = Translate(definition.Unit);
            if (definition.Choices != null)
                for (int i = 0; i < definition.Choices.Length; ++i)
                    definition.Choices[i] = Translate(definition.Choices[i]);
        }

        internal static string Translate(string value)
        {
            if (!_english || string.IsNullOrEmpty(value)) return value;
            string result;
            if (Exact.TryGetValue(value, out result)) return result;
            result = value;
            foreach (KeyValuePair<string, string> pair in Fragments) result = result.Replace(pair.Key, pair.Value);
            return result;
        }

        internal static void Apply(Control root)
        {
            if (!_english || root == null) return;
            root.Text = Translate(root.Text);
            ComboBox combo = root as ComboBox;
            if (combo != null)
            {
                int selected = combo.SelectedIndex;
                for (int i = 0; i < combo.Items.Count; ++i)
                    if (combo.Items[i] is string) combo.Items[i] = Translate((string)combo.Items[i]);
                if (selected >= 0 && selected < combo.Items.Count) combo.SelectedIndex = selected;
            }
            foreach (Control child in root.Controls) Apply(child);
        }

        private static string ReadIniValue(string path, string section, string key)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return string.Empty;
            string current = string.Empty;
            foreach (string raw in File.ReadAllLines(path, Encoding.Default))
            {
                string line = raw.Trim();
                if (line.StartsWith("[") && line.EndsWith("]")) current = line.Substring(1, line.Length - 2).Trim();
                else if (string.Equals(current, section, StringComparison.OrdinalIgnoreCase))
                {
                    int equals = line.IndexOf('=');
                    if (equals > 0 && string.Equals(line.Substring(0, equals).Trim(), key, StringComparison.OrdinalIgnoreCase))
                        return line.Substring(equals + 1).Trim();
                }
            }
            return string.Empty;
        }
    }

    internal static class LocalizedMessageBox
    {
        internal static DialogResult Show(IWin32Window owner, string text, string caption,
            MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            return MessageBox.Show(owner, UiLanguage.Translate(text), UiLanguage.Translate(caption), buttons, icon);
        }
    }
}
