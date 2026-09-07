import { boot } from "quasar/wrappers";

export default boot(({ app }) => {
  // app.config.errorHandler = (err, instance, info) => { console.log("vue error occured. here we can send
  // the log to api"); console.log(err); console.log(instance); console.log(info); };

  // app.config.warnHandler = (msg, instance, trace) => { console.log("vue warning occured. here we can send
  // the log to api"); console.log(msg); console.log(instance); console.log(trace); };

  // window.onerror = function (message, source, lineno, colno, error) { console.log("window error occured.
  // here we can send the log to api"); console.log("Exception: ", error); };
});
