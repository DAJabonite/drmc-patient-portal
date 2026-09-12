// DRMC Patient Portal — client behaviors.
// Philippine Standard Time (UTC+8) clock.

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

        var hours = pst.getHours();
        var minutes = pad(pst.getMinutes());
        var seconds = pad(pst.getSeconds());
        var ampm = hours >= 12 ? "PM" : "AM";
        hours = hours % 12;
        hours = hours ? hours : 12; // 0 becomes 12

        var dateStr = days[pst.getDay()] + ", " + months[pst.getMonth()] + " " + pst.getDate() + ", " + pst.getFullYear();
        var timeStr = hours + ":" + minutes + ":" + seconds + " " + ampm;

        el.textContent = dateStr + ", " + timeStr;
    }

    if (document.getElementById("govphClock")) {
        updateClock();
        setInterval(updateClock, 1000);
    }
})();
