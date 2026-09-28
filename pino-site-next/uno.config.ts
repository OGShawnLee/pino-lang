import {
  defineConfig,
  presetIcons,
  presetTypography,
  transformerDirectives,
  transformerVariantGroup,
  presetWind3,
} from 'unocss';

export default defineConfig({
  presets: [
    presetWind3(),
    presetIcons({
      scale: 1.2,
      warn: true,
    }),
    presetTypography(),
  ],
  transformers: [
    transformerDirectives(),
    transformerVariantGroup(),
  ],
  theme: {
    colors: {
      marigold: {
        50: '#FDF9F2',
        100: '#FAF1E3',
        200: '#F5E1C3',
        300: '#EFCE9E',
        400: '#E8B66F',
        500: '#E59728', // Calzado alegre y soles dorados
        600: '#CA7E26',
        700: '#A45D17',
        800: '#7E4211',
        900: '#5A2E0B',
      },
      cobalt: {
        50: '#F2F4FD',
        100: '#E3E8FA',
        200: '#C7D2F5',
        300: '#9FB2EC',
        400: '#6985DE',
        500: '#3B57C4', // Azul ultramarino elegante y técnico
        600: '#283E9C',
        700: '#1E2F7D',
        800: '#16225B',
        900: '#0F163D',
      },
      botanical: {
        50: '#F2F7F4',
        100: '#E2ECE5',
        200: '#C3D8CB',
        300: '#9DBFB0',
        400: '#6FA08B',
        500: '#2D6A4F', // Verde bosque / pino / naturaleza
        600: '#22553E',
        700: '#19412F',
        800: '#112D20',
        900: '#0A1E15',
      },
      blossom: {
        50: '#FFF5F6',
        100: '#FEE7E9',
        200: '#FCD1D5',
        300: '#F8B1B8',
        400: '#F18C97', // Rosa conejo Pino / floración
        500: '#E56674',
        600: '#CC4655',
        700: '#A8313E',
        800: '#82232E',
        900: '#5C161E',
      },
      oat: {
        50: '#FAF7F2', // Lienzo base cálido y artesanal
        100: '#F4EFE7',
        200: '#ECE3D7', // Bordes sutiles y divisores
        300: '#DECFC0',
        400: '#CAB6A2',
        500: '#B09780',
        600: '#8F755F',
        700: '#6E5643',
        800: '#4D3A2B',
        900: '#2C2218', // Tipografía oscura cálida espresso
      },
      pine: {
        50: '#f2f9f4',
        100: '#e8f5ec',
        200: '#c5e6ce',
        300: '#94d1a5',
        400: '#38b26e',
        500: '#229453',
        600: '#1a7342',
        700: '#145932',
        800: '#0f4627',
        900: '#0a331c',
      },
      white: '#ffffff',
      black: '#000000',
      transparent: 'transparent',
    },
    fontFamily: {
      sans: ['"Satoshi"', 'system-ui', '-apple-system', 'sans-serif'],
      mono: ['"JetBrains Mono"', 'monospace'],
    },
  },
});
