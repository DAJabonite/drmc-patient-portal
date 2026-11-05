(function ($) {
    "use strict";
    $.validator.addMethod("mustbetrue", function (_value, element) {
        return element.checked === true;
    });
    $.validator.unobtrusive.adapters.addBool("mustbetrue");
})(jQuery);
