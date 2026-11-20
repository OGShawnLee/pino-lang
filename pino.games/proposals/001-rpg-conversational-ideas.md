# Game Proposal 001: RPG Conversational Systems

* **Estado**: Borrador (Draft)
* **Autores**: Shawn Lee & Antigravity
* **Fecha**: 2026-06-07

---

## 1. Introducción (Introduction)
Esta propuesta detalla tres conceptos de juegos RPG conversacionales con menús ramificados complejos diseñados para ejecutarse de forma interactiva en la terminal usando Pino Lang. 

El objetivo es expandir la colección de juegos oficiales (`pino.games/`) con una aventura orientada a la narrativa, similar a clásicos como *Jade Empire*, *KOTOR* y novelas visuales interactivas, demostrando la expresividad sintáctica y capacidades lógicas de Pino.

---

## 2. Conceptos de Juegos Propuestos (Proposed Game Concepts)

### Concepto A: "Jade Temple: Path of the Spirit Fist"
Un RPG de artes marciales y mitología china fantástica fuertemente inspirado en *Jade Empire*.

* **Lore**: El protagonista es un estudiante de artes marciales en el Templo del Espíritu Imperial. Tras el secuestro de su maestro por orden de los Inquisidores de Jade, el protagonista debe explorar el valle, negociar con deidades locales y espíritus descarriados, y elegir su propio camino filosófico.
* **Mecánica Conversacional**:
  * **Alineación de Palma Abierta vs. Puño Cerrado**: Las respuestas altruistas o compasivas otorgan puntos de *Palma Abierta*. Las respuestas pragmáticas, violentas o basadas en el poder personal otorgan puntos de *Puño Cerrado*.
  * **Habilidades Sociales**: Opciones de conversación bloqueadas tras atributos como **Razón (Reason)**, **Intimidación (Intimidation)** o **Encanto (Charm)**.
* **Combate por Turnos**:
  * Combates dinámicos contra espíritus y guardias del imperio usando tres estilos marciales con mecánicas de ventaja mutua (ej. *Iron Fist* rompe guardia, *Viper Strike* esquiva y envenena, *Spirit Shield* absorbe Chi).

---

### Concepto B: "Centauri Station: Neon Diplomat"
Una aventura conversacional de ciencia ficción de corte cyberpunk e intriga política.

* **Lore**: En la mega-estación comercial orbital Centauri-9, las tensiones entre la corporación minera terráquea *Gliese Mining Corp* y la guerrilla rebelde de *Outlaws* está a punto de desatar un bombardeo orbital. Juegas como un mediador neutral ("Negociador") cuya única arma es la persuasión.
* **Mecánica Conversacional**:
  * **Gestión de Facciones**: Mantener un medidor de reputación con cada facción. Favorecer a un bando en una disputa cerrará las puertas comerciales con el otro.
  * **Registro de Mentiras (Lie Tracking)**: El script registra las respuestas falsas dadas a ciertos personajes en una estructura de memoria. Si un NPC te interroga posteriormente y te contradices, tu reputación caerá drásticamente o te arrestarán.
* **Mecánica de Juego**:
  * No hay batallas físicas. El progreso y la supervivencia del personaje dependen enteramente de debates, sobornos, hackeos y recopilación de contrabando de información.

---

### Concepto C: "Court of Whispers: The Warlock's Oath"
Un thriller de misterio y conspiración medieval en un reino de fantasía oscura.

* **Lore**: El Gran Emperador Hechicero ha sido asesinado en sus aposentos privados. Eres el Inquisidor de la Corte y tienes 3 días para encontrar al culpable entre los nobles antes de que estalle una guerra civil por la corona.
* **Mecánica Conversacional**:
  * **Sistema de Rumores y Pistas**: Hablar con ciertos criados u observar habitaciones te otorga "pistas" que se guardan como ítems de inventario o en vectores. Puedes usar estas pistas en los diálogos con los nobles sospechosos para confrontar sus testimonios o chantajearlos.
  * **Tiempo Limitado**: Cada viaje entre las salas de la corte o cada interrogatorio consume "Tiempo" (Focus). El jugador debe seleccionar sabiamente a quién interrogar.

---

## 3. Arquitectura del Sistema de Diálogos en Pino Lang

Para representar árboles de diálogo dinámicos y ramificados sin escribir código espagueti, implementaremos la siguiente arquitectura estructurada de datos en Pino:

```pino
# Representa una opción de respuesta seleccionable por el jugador
struct DialogOption {
  text string
  next_node_idx int
  required_alignment int      # >0 para Palma, <0 para Puño
  required_charm int          # Nivel de encanto requerido para intentar
  success_chance int          # Probabilidad de convencer (0-100)
}

# Representa un bloque de conversación de un NPC
struct DialogNode {
  id int
  npc_name string
  dialog_text string
  options []DialogOption      # Opciones disponibles
}

# Representa el estado del diálogo
struct ConversationState {
  current_node_idx int
  is_active bool
}
```

### Flujo de Ejecución (Game Loop):
1. El juego busca en un vector global `[]DialogNode` el nodo correspondiente al `current_node_idx`.
2. Muestra en pantalla el diálogo del NPC.
3. Filtra y muestra en pantalla solo las opciones que el jugador cumple (según su alineación, nivel de encanto y estado).
4. El jugador introduce su selección mediante `readline()`.
5. Si la opción seleccionada tiene una probabilidad de éxito (ej. `success_chance = 60`), el juego hace una tirada matemática con `rand(100)` para bifurcar al nodo de éxito o al nodo de fracaso (combate o ira del NPC).
