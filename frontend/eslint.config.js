// @ts-check
const eslint = require('@eslint/js')
const tseslint = require('typescript-eslint')
const angular = require('angular-eslint')
const eslintPluginPrettierRecommended = require('eslint-plugin-prettier/recommended')

module.exports = tseslint.config(
  {
    files: ['**/*.ts'],
    extends: [
      eslint.configs.recommended,
      ...tseslint.configs.recommended,
      ...tseslint.configs.stylistic,
      ...angular.configs.tsRecommended,
    ],
    processor: angular.processInlineTemplates,
    rules: {
      'no-inline-comments': 'warn',
      'no-console': 'warn',
      'no-var': 'warn',
      '@typescript-eslint/no-unused-vars': 'warn',
      '@typescript-eslint/no-explicit-any': 'off',
      '@typescript-eslint/no-empty-function': 'warn',
      '@angular-eslint/template/alt-text': 'off',
      '@typescript-eslint/no-non-null-asserted-optional-chain': 'off',
      '@typescript-eslint/member-ordering': 'off',
      '@typescript-eslint/naming-convention': [
        'warn',
        {
          selector: 'variable',
          format: ['camelCase', 'UPPER_CASE'],
        },
      ],
      '@angular-eslint/component-class-suffix': 'off',
      '@angular-eslint/directive-selector': [
        'warn',
        {
          type: 'attribute',
          prefix: 'app',
          style: 'camelCase',
        },
      ],
      '@angular-eslint/component-selector': [
        'warn',
        {
          type: 'element',
          prefix: 'app',
          style: 'kebab-case',
        },
      ],
      'prettier/prettier': [
        'error',
        {
          endOfLine: 'auto',
        },
      ],
    },
  },
  {
    files: ['**/*.html'],
    extends: [
      ...angular.configs.templateRecommended,
      ...angular.configs.templateAccessibility,
    ],
    rules: {
      '@angular-eslint/template/prefer-control-flow': 'error',
      '@angular-eslint/template/eqeqeq': 'error',
      '@angular-eslint/template/click-events-have-key-events': 'off',
      '@angular-eslint/template/interactive-supports-focus': 'off',
      '@angular-eslint/template/label-has-associated-control': 'off',
      '@angular-eslint/template/prefer-self-closing-tags': 'warn',
      '@angular-eslint/template/attributes-order': [
        'error',
        {
          alphabetical: false,
          order: [
            'TEMPLATE_REFERENCE',
            'ATTRIBUTE_BINDING',
            'STRUCTURAL_DIRECTIVE',
            'INPUT_BINDING',
            'OUTPUT_BINDING',
            'TWO_WAY_BINDING',
          ],
        },
      ],
      'prettier/prettier': [
        'error',
        {
          endOfLine: 'auto',
          parser: 'angular',
        },
      ],
    },
  },
  eslintPluginPrettierRecommended
)
