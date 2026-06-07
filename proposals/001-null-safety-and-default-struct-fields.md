# RFC 001: Null Safety & Default Struct Field Initializations in Pino

* **Estado**: Borrador (Draft)
* **Autores**: Shawn Lee & Antigravity
* **Fecha**: 2026-06-07

---

## 1. Resumen (Summary)
Esta propuesta define el comportamiento y la sintaxis para mitigar los errores de referencias nulas (`null`) y de atributos no inicializados en las estructuras de datos (`struct`) de Pino Lang. 

Se introducen dos conceptos clave:
1. **Inicialización por defecto automática**: Los structs inicializan sus campos obligatorios con valores seguros por defecto (`""`, `0`, `false`) si son omitidos al instanciar.
2. **Tipos Anulables Explicítos (`type?` / `? type`)**: Sintaxis para declarar propiedades que admiten valor nulo (`null`) de forma explícita e intencional.

---

## 2. Motivación (Motivation)
Actualmente en Pino, la inicialización de structs permite omitir campos en la sintaxis, pero si el código del programa intenta leerlos en tiempo de ejecución, el intérprete arroja un error fatal:
`RUNTIME ERROR: Struct 'Person' has no property 'spouse_name'`.

Esto genera dos problemas graves:
1. **Crashes silenciosos en Gameplay**: Un objeto de juego al que le falta una propiedad menor puede crasear toda la partida de forma repentina.
2. **Inconsistencia de Compilación**: En C# compilado, las propiedades omitidas obtienen el valor por defecto del tipo nativo de .NET, mientras que en el modo interpretado (árbol de sintaxis) explotan.

Buscamos una solución balanceada: proteger al desarrollador contra excepciones sin sobrecargar la sintaxis de un lenguaje dinámico de scripting.

---

## 3. Diseño Detallado (Detailed Design)

### 3.1 Inicialización de Campos por Defecto
Cuando se declara un struct, todos sus campos no anulables se pre-inicializan con valores seguros por defecto si no son provistos en el inicializador de la instancia.

#### Reglas de Valores por Defecto:
* `string` $\rightarrow$ `""` (String vacío)
* `int` / `float` $\rightarrow$ `0` / `0.0`
* `bool` $\rightarrow$ `false`
* `vector` / `[]type` $\rightarrow$ `[]` (Vector vacío o longitud cero)

#### Ejemplo:
```pino
struct Person {
  name string
  age int
  is_married bool
}

# Inicializamos omitiendo 'age' e 'is_married'
val shawn = Person { name: "Shawn" }

println(shawn:age)        # Imprime: 0
println(shawn:is_married) # Imprime: false
```

---

### 3.2 Campos Anulables Opcionales (`spouse_name? string` o `?string`)
Para los campos que legítimamente pueden no tener un valor asignado (ausencia de valor), se introduce el modificador de tipo anulable (`?`).

#### Sintaxis Propuesta:
```pino
struct Person {
  name string
  is_married bool
  spouse_name? string  # El sufijo '?' indica que admite 'null'
}
```

Al inicializar un struct:
* Si un campo anulable se omite en la instanciación, se inicializa automáticamente con el valor especial `null` en lugar del valor por defecto del tipo base (`""`).

```pino
val person = Person { name: "Shawn", is_married: false }

println(person:spouse_name) # Imprime: null (sin crasear el programa)
```

---

### 3.3 Operador de Navegación Segura (Optional Chaining `?:`)
Para facilitar el uso de variables anulables sin requerir estructuras `if-else` anidadas constantes, se propone el operador `?:` (sustituyendo a `?.` para mantener la consistencia con el operador de acceso convencional `:` de Pino).

#### Sintaxis:
```pino
# Si 'spouse_name' es null, la expresión retorna null de inmediato
# en lugar de arrojar una excepción de acceso nulo.
val len = person?:spouse_name?:len() 
```

---

## 4. Implementación Técnica en el Compilador

### 4.1 En el Intérprete C# (`Evaluator.cs`)
En el constructor `PinoStructInstance(PinoStruct @struct)`:
1. Iterar sobre todos los campos definidos en `@struct.Fields`.
2. Si el campo está marcado como anulable (contiene `?`), inicializar su llave en el diccionario `Fields` con `null`.
3. Si no es anulable, asignarle su valor por defecto correspondiente basándose en su tipo (`string` $\rightarrow$ `""`, `int` $\rightarrow$ `0`, etc.).
4. Sobrescribir estos valores con aquellos suministrados explícitamente en la expresión de instanciación del usuario.

### 4.2 En el Transpilador C# (`Transpiler.cs`)
El transpilador generará los structs de Pino como clases C# con tipos anulables tradicionales de C# (e.g. `string?` para `spouse_name? string`).

### 4.3 En el motor JavaScript (`interpreter.js`)
El diccionario de propiedades del prototipo del objeto JS asignará `null` o los valores por defecto (`""`, `0`, `false`) de forma idéntica en la creación de instancias para asegurar paridad absoluta en la web.

---

## 5. Inconvenientes y Desafíos (Drawbacks & Challenges)
1. **Detección del Token `?:`**: En el Lexer, el operador `?:` debe ser escaneado como un único token compuesto para evitar conflictos de precedencia. Esto facilita la vida del Parser de expresiones en comparación con introducir un operador de punto `.` que el lenguaje no utiliza para accesos.
2. **Uso de Memoria**: Crear diccionarios de propiedades completamente poblados para cada struct consume marginalmente más recursos que los diccionarios dispersos anteriores, aunque la ganancia en estabilidad de ejecución supera este costo para fines de scripting de juegos.
