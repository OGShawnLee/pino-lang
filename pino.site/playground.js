// Playground logic for Pino.site
// Pre-loads templates and handles execution hookups with interpreter.js

const TEMPLATES = {
  hello: `# PinoQuest: Hello World Showcase
val name = "Augustus"
val empire = "Roman"
println("$name was the first emperor of the $empire Empire.")

var cycles = 15
cycles += 35
println("Cycles accumulated: $cycles")
`,

  fibonacci: `# PinoQuest: Fibonacci Sequence
println("Calculating the first 12 Fibonacci numbers:")
var a = 0
var b = 1
for i in 12 {
  println("Fibonacci($i) = $a")
  val temp = a + b
  a = b
  b = temp
}
`,

  structs: `# PinoQuest: Custom Structs & In-place Mutations
struct Player {
  name string
  hp int
  max_hp int
  attack int

  fn take_damage(dmg int) {
    var new_hp = hp - dmg
    hp = if new_hp < 0 then 0 else new_hp
  }

  fn heal(amt int) {
    var new_hp = hp + amt
    hp = if new_hp > max_hp then max_hp else new_hp
  }
}

var hero = Player {
  name: "Marcus"
  hp: 75
  max_hp: 100
  attack: 15
}

println("Initial HP of $(hero:name): $(hero:hp)/$(hero:max_hp)")
println("Marcus takes 30 damage in combat!")
hero:take_damage(30)
println("Current HP: $(hero:hp)")

println("Marcus drinks a health potion restoring 40 HP!")
hero:heal(40)
println("Recovered HP: $(hero:hp)/$(hero:max_hp)")
`,

  match: `# PinoQuest: Pattern Matching (Match-When)
val planet = "Mars"
println("Scanning atmospheric conditions on planet: $planet")

match planet {
  when "Earth" {
    println("Gravity: 1.0G")
    println("Status: Perfect Atmosphere")
  }
  when "Mars", "Venus" {
    println("Gravity: Hostile")
    println("Status: Requires breathing apparatus")
  }
  else {
    println("Gravity: Unknown")
    println("Status: Fatal - Atmosphere incompatible")
  }
}
`
};

document.addEventListener('DOMContentLoaded', () => {
  const codeEditor = document.getElementById('code-editor');
  const runBtn = document.getElementById('run-btn');
  const clearBtn = document.getElementById('clear-btn');
  const consoleOutput = document.getElementById('console-output');
  const templateSelect = document.getElementById('template-select');
  const copyBtn = document.getElementById('copy-btn');

  // Load Initial Template
  codeEditor.value = TEMPLATES.hello;

  // Change Template Handler
  templateSelect.addEventListener('change', (e) => {
    const selected = e.target.value;
    if (TEMPLATES[selected]) {
      codeEditor.value = TEMPLATES[selected];
      clearConsole();
    }
  });

  // Run Code Handler
  runBtn.addEventListener('click', () => {
    clearConsole();
    appendOutput(`[SYSTEM] Compiling and executing pino program...\n`);
    const code = codeEditor.value;

    // Capture standard output
    const onOutput = (text) => {
      appendOutput(text);
    };

    // Capture standard input using prompt
    const onInput = () => {
      const input = prompt("Enter input value:");
      appendOutput(`${input}\n`);
      return input || "";
    };

    // Run program execution
    setTimeout(() => {
      runPinoCode(code, onOutput, onInput);
      appendOutput(`\n[SYSTEM] Process finished with exit code 0.\n`);
    }, 50);
  });

  // Clear Console Handler
  clearBtn.addEventListener('click', () => {
    clearConsole();
  });

  // Copy Code Handler
  copyBtn.addEventListener('click', () => {
    navigator.clipboard.writeText(codeEditor.value).then(() => {
      const originalText = copyBtn.innerText;
      copyBtn.innerText = "Copied!";
      setTimeout(() => {
        copyBtn.innerText = originalText;
      }, 2000);
    });
  });

  function clearConsole() {
    consoleOutput.innerHTML = '';
  }

  function appendOutput(text) {
    // Escape HTML to prevent injection
    const escaped = text
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;");
    
    consoleOutput.innerHTML += escaped;
    // Auto scroll to bottom
    consoleOutput.scrollTop = consoleOutput.scrollHeight;
  }
});
