# Game Proposal 003: StarPino Odyssey

* **Estado**: Borrador (Draft)
* **Autores**: Shawn Lee & Antigravity
* **Fecha**: 2026-06-07

---

## 1. Visión General (Overview)
Este documento detalla la visión de diseño para el simulador de exploración espacial retro **StarPino Odyssey** y esboza las mecánicas e historias futuras propuestas para enriquecer el universo de juego.

*StarPino Odyssey* es un juego de exploración, comercio y supervivencia en consola basado en texto, escrito en **Pino Lang**. La experiencia central combina la gestión de recursos (combustible, casco, escudo, créditos) con decisiones narrativas de alto riesgo, progresión de habilidades del piloto y combates tácticos espaciales.

---

## 2. Estado Actual de las Mecánicas - v1.2 (Current Mechanics State)
- **Vuelo e Hiperespacio**: Consumo de combustible dinámico entre sectores con riesgos de viajes (anomalías, tormentas de asteroides).
- **Minería de Asteroides**: Sistema de extracción con tres niveles de profundidad. A mayor profundidad, mayor recompensa de minerales (Mina de Mena y Agua), pero con riesgos incrementados de bolsas de gas inflamables.
- **System de Progresión (Progression System)**:
  - **Mining Skill**: Aumenta el rendimiento y reduce las probabilidades de daño por explosión en las minas.
  - **Charisma Skill**: Aumenta la probabilidad de obtener rumores sobre vetas ricas y coordenadas secretas de doble producción.
- **Miners' Lounge**: Interacciones en tabernas espaciales que otorgan XP, pero con el peligro del *Blackout* (15% de probabilidad de desmayarse y sufrir robos de créditos).
- **El Camino del Proscrito (Outlaw)**:
  - Destruir piratas genera *Intimidación*. Con suficiente reputación, el Sindicato te recluta.
  - Al ser pirata, puedes asaltar cargueros civiles para obtener grandes sumas de botín, a riesgo de desatar emboscadas de Cruceros de Batalla de la Federación.
  - Las estaciones gubernamentales te cierran el paso a menos que sobornes a los guardias.

---

## 3. Futuras Líneas de Desarrollo (Sidelined Events)

### A. Alistamiento en la Vanguardia (Historia Militar)
* **Activación**: Derrotar a un interceptor pirata cerca de una estación espacial en un sector de bajo peligro (e.g. Sol Station o Gliese Icefield).
* **Mecánica**:
  - Al ganar el combate en un área civil, se activa un evento especial: un inspector militar de la Federación que observaba la batalla te felicita por radio: *"Piloto, esa maniobra de evasión ha sido impresionante. La Flota necesita gente con tu talento."*
  - Desbloquea misiones de escolta de convoyes o de patrullaje de fronteras.
  - Al completar misiones militares, progresas en la **Reputación de la Federación**, lo que te permite unirte formalmente a la **Vanguardia de la Tierra**.
  - **Recompensa final**: Acceso gratuito a las bahías de Sol Station, armamento militar de plasma de alto daño y una línea alternativa de victoria al ser promovido a Comandante de la Flota de Defensa.

### B. El Romance de la Nobleza (Escape por Matrimonio)
* **Activación**: Visitar el Miners' Lounge o el Sector de Sol Station con altos niveles de la habilidad **Charisma**.
* **Mecánica**:
  - Te encuentras con una dama/caballero perteneciente a una influyente familia noble de la Tierra que se encuentra de viaje diplomático en los sectores exteriores.
  - A través de eventos de diálogo basados en chequeos de **Charisma**, puedes cortejar al personaje de la nobleza.
  - Deberás impresionarle con historias de tus viajes (requiere nivel de Minería) o defendiéndole de ataques piratas en misiones específicas.
  - **Final Alternativo**: Si logras consolidar la relación y proponer matrimonio, ganas la partida automáticamente. En lugar de comprar un costoso Warp Core (800 CR), regresas a la Tierra a bordo de un yate privado de lujo como parte de la alta sociedad terrestre.

### C. Intriga del Agente Encubierto (Sabotaje y Contrabando)
* **Activación**: Visitar estaciones espaciales teniendo alineamiento de Sindicato (*Outlaw*) o interactuar con desconocidos en la taberna.
* **Mecánica**:
  - Conoces a un agente encubierto del Sindicato de Piratas que opera bajo una fachada legal en la estación.
  - Si tu **Charisma** es alto:
    - El agente te recluta para trabajos de infiltración y contrabando de alta seguridad (llevar mercancía prohibida a Sol Station evadiendo escáneres).
  - Si tu **Charisma** es bajo o demuestras hostilidad:
    - El agente considerará que eres una amenaza para la célula secreta y planeará tu eliminación.
    - Esto desencadena un combate cerrado en los distritos de carga de la estación (combate cara a cara utilizando coberturas y pistolas láser en lugar de naves). Si vences, desmantelas la célula; si fallas, sabotean tu nave reduciendo tu combustible o dañando tu casco antes del despegue.

---

## 4. Expansiones Generales de Sistema (General System Expansions)
1. **Personalización de la Nave**: Comprar componentes en el puerto comercial como *Escudos Deflectores de Iones*, *Láseres de Pulso Pesado* o *Tanques de Combustible Ampliados*.
2. **Economía Dinámica**: Los precios de compra de mineral y agua fluctúan dependiendo de qué tan recientemente hayas explotado las minas de ese sector (simulando oferta y demanda).
3. **Métricas de Facción**: Barra visual de reputación entre la Federación Terrestre y el Sindicato de Piratas que altera cómo te tratan los NPC a lo largo del mapa.
