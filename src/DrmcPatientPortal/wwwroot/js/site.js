// DRMC Patient Portal — client behaviors.
// Philippine Standard Time (UTC+8) clock for the GOVPH utility bar.

(function () {
    "use strict";

    function pad(n) { return n < 10 ? "0" + n : "" + n; }

    function updateClock() {
        var el = document.getElementById("govphClock");
        if (!el) { return; }
        // Philippine Standard Time is fixed at UTC+8 (no DST).
        var now = new Date();
        var utc = now.getTime() + (now.getTimezoneOffset() * 60000);
        var pst = new Date(utc + (8 * 3600000));

        var months = ["January", "February", "March", "April", "May", "June",
            "July", "August", "September", "October", "November", "December"];
        var days = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

        var date = days[pst.getDay()] + ", " + months[pst.getMonth()] + " " + pst.getDate() + ", " + pst.getFullYear();
        var time = pad(pst.getHours()) + ":" + pad(pst.getMinutes()) + ":" + pad(pst.getSeconds());
        el.textContent = date + "  " + time;
    }

    updateClock();
    setInterval(updateClock, 1000);
})();
