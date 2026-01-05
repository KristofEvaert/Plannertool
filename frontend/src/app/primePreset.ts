import { definePreset } from '@primeng/themes';
import Aura from '@primeng/themes/aura';

export const primePreset = definePreset(Aura, {
  semantic: {
    primary: {
      50: '#f2f8fc',
      100: '#c2dcee',
      200: '#91c1e1',
      300: '#61a6d4',
      400: '#308ac6',
      500: '#006fb9',
      600: '#005e9d',
      700: '#004e82',
      800: '#003d66',
      900: '#002c4a',
      950: '#001c2e',
    },
    colorScheme: {
      light: {
        primary: {
          color: '{primary.500}',
          contrastColor: '#ffffff',
          hoverColor: '{primary.900}',
          activeColor: '{primary.800}',
        },
        highlight: {
          background: '{primary.950}',
          focusBackground: '{primary.700}',
          color: '#ffffff',
          focusColor: '#ffffff',
        },
      },
      dark: {
        primary: {
          color: '{primary.50}',
          contrastColor: '{primary.950}',
          hoverColor: '{primary.100}',
          activeColor: '{primary.200}',
        },
        highlight: {
          background: 'rgba(250, 250, 250, .16)',
          focusBackground: 'rgba(250, 250, 250, .24)',
          color: 'rgba(255,255,255,.87)',
          focusColor: 'rgba(255,255,255,.87)',
        },
      },
    },
  },
  components: {
    menubar: {
      root: {
        background: '{menubarbg}',
      },
    },
    paginator: {
      root: {
        padding: '0.5rem 0',
      },
    },
  },
});
