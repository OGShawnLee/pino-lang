# Game Proposal 002: Jade Temple RPG Expansion

* **Estado**: Borrador (Draft)
* **Autores**: Shawn Lee & Antigravity
* **Fecha**: 2026-06-07

---

## 1. Resumen Ejecutivo (Executive Summary)
Esta propuesta detalla la expansión del sistema de juego de *Jade Temple: Path of the Spirit Fist* (`pino.games/jade_temple.pino`). Tras consolidar un sistema de combate estratégico y balanceado, la siguiente fase de desarrollo se enfoca en profundizar en los elementos RPG clásicos: exploración del mundo semiabierto, un inventario de consumibles y amuletos, comercio con NPCs, y un diario de misiones (Quest Log) con consecuencias narrativas basadas en la alineación moral del jugador (Palma Abierta / Puño Cerrado).

---

## 2. Nuevas Zonas y Exploración (New Districts & Exploration)

Proponemos añadir dos nuevas ubicaciones accesibles desde el mapa central (**Imperial Plaza**):

### A. Los Bajos Fondos (The Underbelly / Slums)
Un distrito oscuro y peligroso, controlado por sindicatos locales de luchadores.
* **Actividades**:
  * **La Arena Clandestina**: Desafíos de combate consecutivos contra luchadores callejeros de nivel ascendente para ganar Créditos y reputación de Puño Cerrado.
  * **Mercader de Contrabando**: Un comerciante sombrío que vende pociones prohibidas y el manuscrito del estilo *Dire Viper* (por si el jugador decidió no extorsionar a Maestro Jiang).
* **Diálogos**: Opciones fuertes de intimidación y chantaje.

### B. Las Ruinas del Templo Ancestral (Ancient Jade Ruins)
Un cementerio espiritual custodiado por guerreros fantasmales corruptos por la energía descontrolada del Corazón de Jade.
* **Actividades**:
  * **Purificación de Espíritus**: Combates contra fantasmas agresivos donde derrotarlos pacíficamente (usando estilos específicos o canalizando Chi) otorga puntos de Palma Abierta.
  * **Búsqueda de Reliquias**: Encontrar el Medallón de Master Radiant, necesario para completar su quest secundaria.

---

## 3. Sistema de Inventario y Tienda (Inventory & Shop System)

Para dar más utilidad a los Créditos (CR) y permitir estrategias avanzadas en combates largos, introduciremos un inventario simple mediante variables y flags dentro del struct `Player`, además de un NPC Mercader en la Plaza Imperial.

### Consumibles de Combate:
* **Ginseng Tonic**: Restores 40 HP en combate (sin coste de Chi).
* **Chi Elixir**: Restores 20 Chi en combate.
* **Focus Brew**: Restores 20 Focus en combate.

### Amuletos y Equipamiento (Bufos Pasivos):
* **Open Palm Amulet**: Aumenta en un 25% la ganancia de alineación Open Palm en las misiones.
* **Closed Fist Wrap**: Añade +3 de daño base a todos los estilos físicos (*Iron Fist*, *Tiger Claw*).
* **Jade Lotus Ring**: Reduce el coste de Chi de todos los estilos espirituales en 3 puntos.

---

## 4. Diario de Misiones (Quest Log)

Implementaremos un sistema para registrar el progreso de misiones principales y secundarias en el struct `Player`, desbloqueando recompensas especiales.

### Misiones Propuestas:
1. **"Radiant's Justice" (Principal)**:
   * **Objetivo**: Descubrir la verdad tras el asesinato de Master Radiant.
   * **Resolución Open Palm**: Desafiar y derrotar formalmente a Jiang, liberando el espíritu de Radiant.
   * **Resolución Closed Fist**: Chantajear a Jiang para obtener su poder y silenciar al rebelde Song.
2. **"The Slums Champion" (Secundaria)**:
   * **Objetivo**: Ganar 3 combates seguidos en la Arena Clandestina.
   * **Recompensa**: 50 Créditos y el *Closed Fist Wrap*.
3. **"Spiritual Balance" (Secundaria)**:
   * **Objetivo**: Purificar 3 espíritus corruptos en las Ruinas Ancestrales.
   * **Recompensa**: +15 HP Máximo permanente y el *Open Palm Amulet*.

---

## 5. Estructuras de Datos Propuestas (Proposed Data Structures)

Para mantener el código eficiente y compatible con el intérprete de Pino Lang, expandiremos el struct `Player` para albergar el inventario y misiones como campos directos:

```pino
struct Player {
  name string
  hp int
  max_hp int
  chi int
  max_chi int
  focus int
  max_focus int
  alignment int
  reason int
  intimidation int
  charm int
  style_idx int
  has_dire_viper bool
  credits int

  # --- INVENTORY SYSTEM ---
  tonics_count int           # Ginseng Tonics
  elixirs_count int          # Chi Elixirs
  brews_count int            # Focus Brews
  has_palm_amulet bool       # Passive buff
  has_fist_wrap bool         # Passive buff
  has_lotus_ring bool        # Passive buff

  # --- QUEST LOG SYSTEM ---
  quest_radiant_state int    # 0: Not Started, 1: Active, 2: Resolved
  quest_arena_wins int       # Counter for Slums secondary quest (0 to 3)
  quest_spirit_purified int  # Counter for Ruins secondary quest (0 to 3)
}
```

### Métodos de Estructura Propuestos para Gestión:
```pino
  # Consumir un ítem durante el combate
  fn use_tonic() {
    if tonics_count > 0 {
      tonics_count = tonics_count - 1
      hp = hp + 40
      if hp > max_hp {
        hp = max_hp
      }
      println("🧪 You drink a Ginseng Tonic and restore 40 HP!")
      return true
    }
    println("❌ No Ginseng Tonics left!")
    return false
  }
```
