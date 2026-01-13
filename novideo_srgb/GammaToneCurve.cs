using System;

namespace novideo_srgb
{
    public class GammaToneCurve : ToneCurve
    {
        private readonly double _gamma;
        private double _a = 1;
        private double _b;
        private readonly double _c;

        private readonly double _trc_black;
        private readonly double _tag_black;

        public unsafe GammaToneCurve(double gamma, double trc_black = 0, double tag_black = 0, double outputOffset = 1, bool relative = false)
        {
            _trc_black = trc_black;
            _tag_black = tag_black;
            if (tag_black == 0)
            {
                _gamma = gamma;
                return;
            }

            if (outputOffset == 1)
            {
                _gamma = !relative
                    ? gamma
                    : Math.Log((tag_black - 1) * Math.Pow(2, gamma) / (tag_black * Math.Pow(2, gamma) - 1), 2);
                _a = 1 - tag_black;
                _c = tag_black;
            }
            else
            {
                var outBlack = outputOffset * tag_black;
                var btWhite = 1 - outBlack;
                var btBlack = tag_black - outBlack;
                _c = outBlack;

                if (!relative)
                {
                    _gamma = gamma;
                    CalculateBT1886(btWhite, btBlack);
                }
                else
                {
                    // assume sane values for black and gamma
                    double lowD = 1;
                    double highD = 8;

                    // what the hell
                    var low = *(ulong*)&lowD;
                    var high = *(ulong*)&highD;

                    var target = Math.Pow(0.5, gamma);

                    while (true)
                    {
                        var mid = (low + high) / 2;
                        _gamma = *(double*)&mid;
                        CalculateBT1886(btWhite, btBlack);
                        var sample = SampleAt(0.5);
                        if (sample == target || low == mid || high == mid)
                        {
                            break;
                        }

                        if (sample > target)
                        {
                            low = mid;
                        }
                        else
                        {
                            high = mid;
                        }
                    }
                }
            }
        }

        private void CalculateBT1886(double white, double black)
        {
            var lwg = Math.Pow(white, 1 / _gamma);
            var lbg = Math.Pow(black, 1 / _gamma);
            _a = Math.Pow(lwg - lbg, _gamma);
            _b = lbg / (lwg - lbg);
        }

        public double SampleAt(double x)
        {
            if (x >= 1) return 1;
            var compensation = _tag_black - _trc_black;
            var res = _a * Math.Pow(Math.Max(x + _b, 0), _gamma) + _c;
            return (res - compensation) / (1 - compensation);
        }

        public double SampleInverseAt(double x)
        {
            if (_a != 1) throw new NotSupportedException();
            if (x >= 1) return 1;
            return Math.Pow(x, 1 / _gamma);
        }
    }
}