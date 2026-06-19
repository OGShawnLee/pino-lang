# RFC 002: Optimización del Compilador e Intérprete mediante Type Checking Estático en Pino

* **Estado**: Borrador (Draft)
* **Autores**: Shawn Lee & Antigravity
* **Fecha**: 2026-06-10

---

## 1. Resumen (Summary)
Esta propuesta define una serie de optimizaciones de rendimiento y diseño de backend para el lenguaje Pino Lang, aprovechando la información de tipos resuelta y validada estáticamente por el nuevo componente `Checker`. 

Se proponen tres áreas clave de optimización:
1. **Traspilado Fuertemente Tipado**: Eliminar el uso de `dynamic` en el generador de código C# (`Transpiler.cs`), reemplazándolo por tipos nativos y firmas directas de .NET.
2. **Acceso a Miembros por Índice Fijo**: Sustituir las búsquedas de propiedades basadas en cadenas de texto en las instancias de structs por acceso directo en memoria a través de índices numéricos precalculados.
3. **Optimización de Flujo y Devirtualización**: Simplificar llamadas a interfaces con un único implementador (devirtualización) y remover ramas de ejecución inaccesibles identificadas estáticamente.

---

## 2. Motivación (Motivation)
Con la reciente introducción del paso de verificación estática de tipos (`Checker`), el compilador de Pino ahora valida la consistencia de tipos, firmas de funciones y compatibilidad de interfaces antes de ejecutar el programa. Sin embargo, el intérprete y el transpilador actuales siguen utilizando técnicas de tipado dinámico:
* En C# compilado (`Transpiler.cs`), todas las variables y accesos a métodos se generan usando el tipo `dynamic`. Esto obliga al runtime de .NET (DLR) a buscar miembros en tiempo de ejecución, lo que añade overhead.
* En el intérprete (`Evaluator.cs` y `interpreter.js`), las propiedades de los structs se guardan en diccionarios/mapas donde las llaves son strings (ej. `fields["name"]`), resultando en búsquedas hash repetitivas durante bucles intensivos.

Dado que la información de tipos ya está disponible de forma estática, podemos transformar estas operaciones en accesos directos y fuertemente tipados, logrando ganancias masivas de rendimiento.

---

## 3. Diseño Detallado (Detailed Design)

### 3.1 Traspilado Fuertemente Tipado (Eliminación de `dynamic`)
En lugar de transpilación genérica a `dynamic`, el compilador usará el mapa de tipos del `Checker` para emitir variables y parámetros con sus tipos nativos de C#:

* `int` $\rightarrow$ `long`
* `float` $\rightarrow$ `double`
* `string` $\rightarrow$ `string`
* `bool` $\rightarrow$ `bool`
* `[]T` $\rightarrow$ `List<T>` o `T[]`
* `map[K, V]` $\rightarrow$ `Dictionary<K, V>`
* `struct S` $\rightarrow$ Generar una clase C# llamada `S` con campos tipados, eliminando el mapeo a través de los wrappers de métodos reflexivos `Program.CallMethod`.

#### Ejemplo de Transpilación Propuesta:
**Código Pino:**
```pino
struct User {
  name string
  age int
}
fn age_in_dog_years(u User) int {
  return u:age * 7
}
```

**Antes (Transpilación Dinámica):**
```csharp
public class User {
  public dynamic name;
  public dynamic age;
}
public static dynamic age_in_dog_years(dynamic u) {
  return u.age * 7;
}
```

**Después (Transpilación Fuertemente Tipada):**
```csharp
public class User {
  public string name;
  public long age;
}
public static long age_in_dog_years(User u) {
  return u.age * 7;
}
```
*Impacto*: Elimina las validaciones en tiempo de ejecución de .NET y permite optimizaciones de compilación agresivas por parte del compilador JIT/AOT de C# (como inlining y optimización de registros).

---

### 3.2 Acceso a Miembros de Structs por Índice Fijo
En la ejecución interpretada del árbol de sintaxis (Tree-Walk), tanto en C# como en JavaScript, los structs se representan mediante diccionarios asociativos.

#### Diseño Propuesto:
1. Durante la compilación/verificación, el compilador procesa `StructDecl` y le asigna a cada campo un índice numérico secuencial (basado en su posición de declaración).
2. La estructura interna de una instancia de struct en el intérprete cambia de un mapa/objeto a una matriz plana indexada:
   * **Antes (JS)**: `this.fields = { name: "Marcus", age: 30 }`
   * **Después (JS)**: `this.fields = ["Marcus", 30]` (donde `name` tiene índice 0 y `age` índice 1).
3. Cualquier expresión de acceso a miembro `u:age` se reescribe o evalúa en base a su índice numérico previamente resuelto por el Type Checker: `u.fields[1]`.

*Impacto*: Convierte la consulta de propiedades de una búsqueda Hash en diccionario (complejidad $O(1)$ con costo de hash alto) a un acceso indexado de arreglo en memoria (complejidad $O(1)$ directo a nivel de puntero/dirección).

---

### 3.3 Devirtualización de Interfaces
Cuando el compilador encuentra una llamada de método a través de un tipo interfaz (como `g:greet()`), el compilador debe emitir una llamada indirecta dinámica.

#### Optimización Estática:
Si el Type Checker escanea el árbol y determina que solo existe una estructura en todo el programa que implementa dicha interfaz (ej. solo `struct User` implementa `Greeter`), el compilador puede reescribir la llamada de forma segura a una llamada directa: `User:greet(g)`.
Esto elimina el salto indirecto de la tabla de vtable, mejorando la predicción de ramas de la CPU.

---

### 3.4 Plegado de Constantes y Eliminación de Código Muerto
El Type Checker puede evaluar de forma segura expresiones constantes en tiempo de compilación:
* `val x = 10 + 20` se reduce directamente a `val x = 30` en el AST.
* `if false { ... }` se descarta por completo del AST para que el intérprete ni siquiera tenga que procesar la sintaxis de las ramas internas en tiempo de ejecución.

---

## 4. Implementación Técnica en el Compilador

### 4.1 Cambios en `Transpiler.cs`
* Integrar el `Checker` como un paso previo en `Program.cs` antes de llamar a `Transpiler.Transpile(...)`.
* Pasar el contexto de símbolos resueltos (`Checker`) al transpilador.
* Modificar la lógica de generación de código en `Transpiler` para mapear los nodos del AST a sus homólogos tipados en C#.

### 4.2 Cambios en `Evaluator.cs` y `interpreter.js`
* Modificar el AST o el nodo de acceso a miembros `BinaryExpression` (operador `:`) para incluir una propiedad interna opcional `ResolvedFieldIndex`.
* Durante la fase del Type Checker, al resolver accesos a miembros en structs conocidos, asignar este `ResolvedFieldIndex`.
* En la fase de evaluación, si `ResolvedFieldIndex` está presente, acceder directamente al índice del array en lugar de hacer la búsqueda por cadena de caracteres.

---

## 5. Inconvenientes y Desafíos (Drawbacks & Challenges)
1. **Pérdida de Flexibilidad Dinámica**: La eliminación de `dynamic` significa que el transpilador será más estricto y requerirá que todos los tipos complejos estén completamente definidos y resueltos.
2. **Complejidad del compilador**: Requiere mantener sincronizados los índices de campos de los structs cuando se usan importaciones entre módulos y namespaces.
3. **Manejo de Tipos "any"**: Para las partes del código que deliberadamente usen variables de tipo `any`, el transpilador aún deberá caer de vuelta a `dynamic` en C# u objetos de clave-valor genéricos en JS, lo que significa que el compilador debe soportar un modelo híbrido.
