# MicMixer

Mischt den Ton eines Fensters (Spotify, Browser mit YouTube, …) in dein Mikrofon.

## Einmalig einrichten
1. **VB-Audio Virtual Cable** installieren (kostenlos): https://vb-audio.com/Cable/ → als Admin `VBCABLE_Setup_x64.exe` → PC neu starten.
2. In Discord / Spiel / OBS als **Mikrofon** `CABLE Output (VB-Audio Virtual Cable)` auswählen.

## Benutzen
1. `MicMixer.exe` aus den Releases starten (braucht die .NET 9 Desktop Runtime) oder selbst bauen (siehe unten).
2. Mikrofon = dein echtes Mikro, Ausgabe = `CABLE Input` (wird automatisch gewählt).
3. **Start** drücken.
4. Modus wählen:
   - **Normal – nur Mikrofon**: alles wie vorher.
   - **Mikrofon + Fenster-Audio**: andere hören dich und die Musik.
   - **Nur Fenster-Audio**: nur die Musik.
5. Fenster auswählen (↻ lädt die Liste neu), Lautstärken anpassen. Umschalten geht live.

Die App muss laufen, solange andere dich hören sollen – sonst ist `CABLE Output` stumm.

Nach der VB-Cable-Installation prüfen, dass Windows nicht `CABLE Input` als Standard-Lautsprecher gesetzt hat – sonst hörst du selbst nichts.

## Tipps für Discord
Rauschunterdrückung auf „Keine“, Echo-Unterdrückung und automatische Verstärkung aus, Eingabeempfindlichkeit manuell ganz nach links.

## Bauen
Windows 10 2004+ und .NET 9 SDK:
```
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```
