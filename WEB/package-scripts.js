export default {
  scripts: {
    push: "npm version patch",
    push_minor: "npm version minor",
    push_major: "npm version major",

    dev: "cross-env QENV=dev quasar dev",
    build_test: "cross-env QENV=test quasar build",
    build_prod: "cross-env QENV=prod quasar build",

    lint: "eslint --ext .js,.vue ./",

    // Refreshes the Google Fonts catalogue the branding editor offers (and the API's copy of it).
    google_fonts: "node scripts/update-google-fonts.mjs"
  }
};
