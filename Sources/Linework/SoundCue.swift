import AppKit

@MainActor
enum SoundCue {
    static func tick() { play("Tink", volume: 0.5) }
    static func chime() { play("Glass", volume: 0.8) }
    /// s.e.a. has something to say. Different from `chime`, which means the pose changed.
    static func note() {
        #if DEBUG
        if DebugSnapshots.active { return }
        #endif
        guard UserDefaults.standard.object(forKey: "linework.sound") as? Bool ?? true else { return }
        play("Ping", volume: 0.4)
    }

    private static func play(_ name: String, volume: Float) {
        // Copies so overlapping cues don't cut each other off.
        guard let sound = NSSound(named: name)?.copy() as? NSSound else { return }
        sound.volume = volume
        sound.play()
    }
}
