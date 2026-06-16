<script setup lang="ts">
  import { ref, onMounted, nextTick } from 'vue';
  import { runPinoCode } from '../utils/interpreter.js';

  const TEMPLATES: Record<string, string> = {
    hello: `# PinoQuest: Hello World Showcase\nval name = "Augustus"\nval empire = "Roman"\nprintln("$name was the first emperor of the $empire Empire.")\n\nvar cycles = 15\ncycles += 35\nprintln("Cycles accumulated: $cycles")\n`,
    fibonacci: `# PinoQuest: Fibonacci Sequence\nprintln("Calculating the first 12 Fibonacci numbers:")\nvar a = 0\nvar b = 1\nfor i in 12 {\n  println("Fibonacci($i) = $a")\n  val temp = a + b\n  a = b\n  b = temp\n}\n`,
    structs: `# PinoQuest: Custom Structs & In-place Mutations\nstruct Player {\n  name string\n  hp int\n  max_hp int\n  attack int\n\n  fn take_damage(dmg int) {\n    var new_hp = hp - dmg\n    hp = if new_hp < 0 then 0 else new_hp\n  }\n\n  fn heal(amt int) {\n    var new_hp = hp + amt\n    hp = if new_hp > max_hp then max_hp else new_hp\n  }\n}\n\nvar hero = Player {\n  name: "Marcus"\n  hp: 75\n  max_hp: 100\n  attack: 15\n}\n\nprintln("Initial HP of $(hero:name): $(hero:hp)/$(hero:max_hp)")\nprintln("Marcus takes 30 damage in combat!")\nhero:take_damage(30)\nprintln("Current HP: $(hero:hp)")\n\nprintln("Marcus drinks a health potion restoring 40 HP!")\nhero:heal(40)\nprintln("Recovered HP: $(hero:hp)/$(hero:max_hp)")\n`,
    match: `# PinoQuest: Pattern Matching (Match-When)\nval planet = "Mars"\nprintln("Scanning atmospheric conditions on planet: $planet")\n\nmatch planet {\n  when "Earth" {\n    println("Gravity: 1.0G")\n    println("Status: Perfect Atmosphere")\n  }\n  when "Mars", "Venus" {\n    println("Gravity: Hostile")\n    println("Status: Requires breathing apparatus")\n  }\n  else {\n    println("Gravity: Unknown")\n    println("Status: Fatal - Atmosphere incompatible")\n  }\n}\n`,
    vectors: `# PinoQuest: Vectors & Functional APIs\nval get_times_it_fn = fn (multiplier int) => fn (it int) => it * multiplier\nval numbers = []int { len: 6, init: it + 1 }\n\nprintln("Original numbers:")\nprintln(numbers)\n\nprintln("Doubled numbers (using currying + map):")\nprintln(numbers:map(get_times_it_fn(2)))\n`
  };

  const code = ref('');
  const logs = ref('');
  const selectedTemplate = ref('hello');
  const isCompiling = ref(false);
  const copyBtnText = ref('Copy');
  const consoleRef = ref<HTMLDivElement | null>(null);

  onMounted(() => {
    // Load default template
    code.value = TEMPLATES.hello;
  });

  const scrollToBottom = () => {
    nextTick(() => {
      if (consoleRef.value) {
        consoleRef.value.scrollTop = consoleRef.value.scrollHeight;
      }
    });
  };

  const clearConsole = () => {
    logs.value = '';
  };

  const onTemplateChange = (event: Event) => {
    const target = event.target as HTMLSelectElement;
    const key = target.value;
    selectedTemplate.value = key;
    if (TEMPLATES[key]) {
      code.value = TEMPLATES[key];
      clearConsole();
    }
  };

  const copyCode = () => {
    if (typeof navigator !== 'undefined') {
      navigator.clipboard.writeText(code.value).then(() => {
        copyBtnText.value = 'Copied!';
        setTimeout(() => {
          copyBtnText.value = 'Copy';
        }, 1500);
      });
    }
  };

  const runCode = () => {
    clearConsole();
    logs.value = '[SYSTEM] Compiling and executing pino program...\n';
    isCompiling.value = true;
    const codeToRun = code.value;

    const onOutput = (text: string) => {
      if (text === '\f') {
        clearConsole();
      } else {
        logs.value += text;
        scrollToBottom();
      }
    };

    const onInput = () => {
      const input = prompt("Enter input value:");
      logs.value += `${input}\n`;
      scrollToBottom();
      return input || "";
    };

    // Run asynchronously to allow loader rendering
    setTimeout(() => {
      try {
        runPinoCode(codeToRun, onOutput, onInput);
        logs.value += `\n[SYSTEM] Process finished with exit code 0.\n`;
      } catch (err: any) {
        logs.value += `\n[ERROR] Compiler execution failed: ${err.message || err}\n`;
      } finally {
        isCompiling.value = false;
        scrollToBottom();
      }
    }, 50);
  };
</script>

<template>
  <div class="bg-bg-secondary/70 backdrop-blur-md border border-border-colored rounded-2xl overflow-hidden shadow-2xl">
    <!-- Toolbar -->
    <div class="flex justify-between items-center px-6 py-3 bg-black/20 border-b border-border-colored">
      <div class="flex items-center gap-1.5">
        <div class="w-3 h-3 rounded-full bg-red-500"></div>
        <div class="w-3 h-3 rounded-full bg-yellow-500"></div>
        <div class="w-3 h-3 rounded-full bg-emerald-500"></div>
        <span class="ml-2 text-xs font-500 text-text-muted font-mono">playground.pino</span>
      </div>
      <div class="flex items-center gap-4">
        <div
          class="relative after:(content-['▾'] absolute right-3 top-1/2 -translate-y-1/2 text-text-muted pointer-events-none)">
          <select :value="selectedTemplate" @change="onTemplateChange"
            class="bg-white/5 border border-border-colored text-text-main pl-3 pr-7 py-1 rounded-md text-xs font-sans cursor-pointer outline-none appearance-none"
            aria-label="Select code template">
            <option value="hello">Hello World</option>
            <option value="fibonacci">Fibonacci Sequence</option>
            <option value="structs">Structs & Mutations</option>
            <option value="match">Match-When Cases</option>
            <option value="vectors">Vectors & Functional APIs</option>
          </select>
        </div>
        <button @click="copyCode"
          class="bg-transparent border-0 text-text-muted cursor-pointer p-1 rounded hover:(text-text-main bg-white/8) text-xs flex items-center gap-1.5 transition">
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"
            stroke-linecap="round" stroke-linejoin="round">
            <rect x="9" y="9" width="13" height="13" rx="2" ry="2"></rect>
            <path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"></path>
          </svg>
          {{ copyBtnText }}
        </button>
      </div>
    </div>

    <!-- Workspace -->
    <div class="grid grid-cols-1 md:grid-cols-2 md:h-[520px] h-auto">
      <!-- Editor Panel -->
      <div class="border-b md:border-b-0 md:border-r border-border-colored relative h-[350px] md:h-full">
        <textarea v-model="code" spellcheck="false" aria-label="Pino code editor"
          class="w-full h-full bg-transparent border-0 resize-none outline-none text-gray-200 font-mono text-[0.95rem] p-6 leading-relaxed tab-size-2"></textarea>
      </div>

      <!-- Terminal Panel -->
      <div class="bg-[#090c10] flex flex-col h-[280px] md:h-full">
        <div class="px-5 py-2.5 bg-black/20 border-b border-border-colored flex justify-between items-center">
          <span class="font-mono text-xs font-500 text-accent-green">Pino Compiler CLI</span>
          <button @click="clearConsole"
            class="bg-transparent border-0 text-text-muted cursor-pointer px-2 py-0.5 rounded hover:(text-text-main bg-white/8) text-xs transition">
            Clear
          </button>
        </div>
        <div ref="consoleRef"
          class="flex-grow p-5 overflow-y-auto font-mono text-[0.9rem] leading-relaxed text-[#a3b3c6] whitespace-pre-wrap scroll-smooth">
          {{ logs }}</div>
      </div>
    </div>

    <!-- Footer -->
    <div class="flex justify-end px-6 py-3 bg-black/20 border-t border-border-colored">
      <button @click="runCode" :disabled="isCompiling"
        class="bg-accent-green text-bg-primary font-600 border-0 px-6 py-2 rounded-lg cursor-pointer flex items-center gap-1.5 transition duration-200 disabled:opacity-60 hover:enabled:(bg-accent-green-hover shadow-[0_0_12px_rgba(16,185,129,0.25)])">
        <svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor">
          <polygon points="5 3 19 12 5 21 5 3"></polygon>
        </svg>
        Compile & Run
      </button>
    </div>
  </div>
</template>
