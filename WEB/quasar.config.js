import { defineConfig } from "#q-app/wrappers";
import path from "path";
import { fileURLToPath } from "url";
import { createRequire } from "module";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const require = createRequire(import.meta.url);

// determine the current environment
const envName = process.env.QENV || "dev";

// dynamically require the environment file
const envConfig = require(`./config/env.${envName}.cjs`);

export default defineConfig((ctx) => {
  return {
    eslint: {
      warnings: true,
      errors: true
    },

    boot: [
      "error",
      "axios",
      "interceptors",
      "branding",
      "title",
      "components",
      "i18n"
    ],

    css: [
      "typography.scss",
      "app.scss",
      "page.scss",
      "custom.scss",
      // The REMS record vocabulary (label / value / subhead), defined once instead of once per screen.
      "rems.scss"
    ],

    extras: [
      "mdi-v5",
      "fontawesome-v6",
      "roboto-font",
      "material-icons-outlined"
    ],

    build: {
      minify: true,
      sourcemap: false,
      target: {
        browser: ["es2019", "edge88", "firefox78", "chrome87", "safari13.1"],
        node: "node16"
      },

      vueRouterMode: "history",
      env: envConfig,
      publicPath: envConfig.BUILD_PUBLIC_PATH,
      ignorePublicFolder: envConfig.IGNORE_PUBLIC_FOLDER,
      distDir: "../publish/spa/" + envConfig.PUBLISH_FOLDER,

      alias: {
        shared: path.join(__dirname, "./src/shared"),
        services: path.join(__dirname, "./src/services"),
        validators: path.join(__dirname, "./src/validators"),
        modules: path.join(__dirname, "./src/modules"),
        dialogs: path.join(__dirname, "./src/dialogs"),
        composables: path.join(__dirname, "./src/composables"),
        utils: path.join(__dirname, "./src/utils")
      },

      vitePlugins: [
        ["@intlify/unplugin-vue-i18n/vite", {
          include: path.resolve(__dirname, "./src/i18n/**")
        }]
      ],
      extendViteConf (viteConf) {
        viteConf.build = {
          ...viteConf.build,
          chunkSizeWarningLimit: 750,
          rollupOptions: {
            output: {
              manualChunks (id) {
                if (!id.includes("node_modules")) return;

                if (id.includes("vue") || id.includes("vue-router") || id.includes("pinia")) {
                  return "vendor-vue";
                }

                if (id.includes("quasar")) {
                  return "vendor-quasar";
                }

                if (
                  id.includes("@fullcalendar/core") ||
                  id.includes("@fullcalendar/vue3") ||
                  id.includes("@fullcalendar/daygrid") ||
                  id.includes("@fullcalendar/timegrid")
                ) {
                  return "vendor-fullcalendar";
                }

                if (
                  id.includes("d3") ||
                  id.includes("d3-org-chart") ||
                  id.includes("d3-flextree")
                ) {
                  return "vendor-d3";
                }

                // fallback
                return "vendor";
              }
            }
          }
        };
      },
    },
    devServer: {
      open: true
    },

    framework: {
      config: {},
      iconSet: "material-icons-outlined",
      plugins: [
        "LocalStorage",
        "Notify",
        "Loading",
        "Dialog",
        "Meta",
        "Cookies",
        "BottomSheet"
      ]
    },

    animations: [],

    ssr: {
      pwa: false,
      prodPort: 3000,
      middlewares: ["render"]
    },

    pwa: {
      workboxMode: "generateSW",
      injectPwaMetaTags: true,
      swFilename: "sw.js",
      manifestFilename: "manifest.json",
      useCredentialsForManifestTag: false
    },

    cordova: {},

    capacitor: {
      hideSplashscreen: true
    },

    electron: {
      inspectPort: 5858,
      bundler: "packager",
      packager: {},
      builder: {
        appId: "EmsPortal"
      }
    },

    bex: {
      contentScripts: ["my-content-script"]
    }
  };
});
